# Proves the kit hooks are wired in settings and behave as claimed, without Claude:
# checks .claude/settings*.json, then feeds the hooks fake inputs.
#   powershell -NoProfile -ExecutionPolicy Bypass -File .claude\hooks\selftest.ps1
$ErrorActionPreference = 'Continue'
$hooks = $PSScriptRoot
$results = @()

function Run-Hook([string]$exe, [string[]]$hookArgs, [string]$json) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $exe
    $psi.Arguments = ($hookArgs | ForEach-Object { '"' + $_ + '"' }) -join ' '
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardError = $true
    $psi.RedirectStandardOutput = $true
    $p = [System.Diagnostics.Process]::Start($psi)
    $p.StandardInput.Write($json); $p.StandardInput.Close()
    $err = $p.StandardError.ReadToEnd(); $null = $p.StandardOutput.ReadToEnd()
    $p.WaitForExit()
    [pscustomobject]@{ Code = $p.ExitCode; Err = $err.Trim() }
}

function Check([string]$name, [int]$expected, $r) {
    $verdict = if ($r.PSObject.Properties['Verdict']) { $r.Verdict } elseif ($r.Code -eq 2) { 'BLOCK' } else { 'ALLOW' }
    $ok = $r.Code -eq $expected
    $script:results += $ok
    $mark = if ($ok) { 'PASS' } else { 'FAIL' }
    Write-Host ("{0}  {1,-5}  {2}" -f $mark, $verdict, $name) -ForegroundColor $(if ($ok) { 'Green' } else { 'Red' })
    if ($r.Err) { Write-Host ("       " + ($r.Err -split "`n")[0]) -ForegroundColor DarkGray }
}

# Git Bash, never WSL's System32\bash.exe (no Git for Windows tools, no dotnet inside WSL).
function Find-GitBash {
    foreach ($git in @(Get-Command git -All -ErrorAction SilentlyContinue | ForEach-Object Source)) {
        $dir = Split-Path $git
        for ($i = 0; $i -lt 3 -and $dir; $i++) {
            if (Test-Path (Join-Path $dir 'bin\bash.exe')) { return (Join-Path $dir 'bin\bash.exe') }
            $dir = Split-Path $dir
        }
    }
    if (Test-Path 'C:\Program Files\Git\bin\bash.exe') { return 'C:\Program Files\Git\bin\bash.exe' }
    Get-Command bash -All -ErrorAction SilentlyContinue | Where-Object Source -notmatch '\\(System32|WindowsApps)\\' | Select-Object -First 1 -ExpandProperty Source
}
$bash = Find-GitBash
if (-not $bash) { Write-Host 'FAIL  Git Bash not found: install Git for Windows' -ForegroundColor Red; exit 1 }

Write-Host "settings (hooks wired in .claude/settings*.json)"
$wired = @{ gate = $false; guard = $false; session = $false }
foreach ($f in 'settings.json', 'settings.local.json') {
    $path = Join-Path (Split-Path $hooks) $f
    if (-not (Test-Path $path)) { continue }
    try { $cfg = Get-Content $path -Raw | ConvertFrom-Json } catch { Write-Host "       $f is not valid JSON" -ForegroundColor DarkGray; continue }
    foreach ($entry in @($cfg.hooks.PreToolUse)) {
        if (-not $entry) { continue }
        $cmds = @($entry.hooks | ForEach-Object command) -join ' '
        if ($entry.matcher -match 'Bash' -and $cmds -match 'test-gate\.sh') { $wired.gate = $true }
        if ($entry.matcher -match 'Edit' -and $cmds -match 'trunk-guard\.ps1') { $wired.guard = $true }
    }
    foreach ($entry in @($cfg.hooks.SessionStart)) {
        if ($entry -and (@($entry.hooks | ForEach-Object command) -join ' ') -match 'session-context\.sh') { $wired.session = $true }
    }
}
Check 'test-gate wired as PreToolUse on Bash' 0 ([pscustomobject]@{ Code = $(if ($wired.gate) { 0 } else { 1 }); Err = ''; Verdict = $(if ($wired.gate) { 'WIRED' } else { 'MISSING' }) })
Check 'trunk-guard wired as PreToolUse on Edit|Write' 0 ([pscustomobject]@{ Code = $(if ($wired.guard) { 0 } else { 1 }); Err = ''; Verdict = $(if ($wired.guard) { 'WIRED' } else { 'MISSING' }) })
Check 'session-context wired as SessionStart' 0 ([pscustomobject]@{ Code = $(if ($wired.session) { 0 } else { 1 }); Err = ''; Verdict = $(if ($wired.session) { 'WIRED' } else { 'MISSING' }) })

$commit = '{"tool_name":"Bash","tool_input":{"command":"git commit -m wip"}}'

Write-Host "test-gate (PreToolUse on git commit)"
$env:KIT_BASE_BRANCH = 'main'
$env:KIT_CURRENT_BRANCH = 'feature/s1-selftest'
$env:KIT_TEST_CMD = 'echo "1 Failed: Create_InvalidAmount_Returns400"; exit 1'
Check 'commit while tests are red' 2 (Run-Hook $bash @("$hooks/test-gate.sh") $commit)
$withOpts = '{"tool_name":"Bash","tool_input":{"command":"git -c user.name=x --no-pager commit -m wip"}}'
Check 'commit with git global options while red' 2 (Run-Hook $bash @("$hooks/test-gate.sh") $withOpts)
$heredoc = '{"tool_name":"Bash","tool_input":{"command":"cat > notes.txt <<EOF\nred step\nEOF"}}'
Check 'other command while tests are red (TDD red step)' 0 (Run-Hook $bash @("$hooks/test-gate.sh") $heredoc)
$described = '{"tool_name":"Bash","tool_input":{"command":"dotnet test | tail -5","description":"Run tests before git commit"}}'
Check 'test run whose description mentions git commit' 0 (Run-Hook $bash @("$hooks/test-gate.sh") $described)
$env:KIT_TEST_CMD = 'exit 0'
Check 'commit while tests are green' 0 (Run-Hook $bash @("$hooks/test-gate.sh") $commit)
$env:KIT_BASE_ON_REMOTE = '1'
$pushSlice = '{"tool_name":"Bash","tool_input":{"command":"git push -u origin feature/s1-selftest"}}'
Check 'push a slice branch' 0 (Run-Hook $bash @("$hooks/test-gate.sh") $pushSlice)
$pushBase = '{"tool_name":"Bash","tool_input":{"command":"git push origin HEAD:main"}}'
Check 'push to the base branch' 2 (Run-Hook $bash @("$hooks/test-gate.sh") $pushBase)
$env:KIT_CURRENT_BRANCH = 'main'
Check 'commit on the base branch (tests green)' 2 (Run-Hook $bash @("$hooks/test-gate.sh") $commit)
Remove-Item Env:KIT_TEST_CMD, Env:KIT_BASE_BRANCH, Env:KIT_CURRENT_BRANCH, Env:KIT_BASE_ON_REMOTE

Write-Host "trunk-guard (PreToolUse on Edit/Write)"
$tmp = Join-Path $env:TEMP ("kit-selftest-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
$main = Join-Path $tmp 'repo'
$wt = Join-Path $tmp 'wt'
New-Item -ItemType Directory -Force (Join-Path $main '.claude'), (Join-Path $main 'src/Api') | Out-Null
Set-Content (Join-Path $main '.claude/trunk-paths.txt') "**/Program.cs`n**/Domain/**"
Set-Content (Join-Path $main 'src/Api/Program.cs') '// app'
git -C $main init -q -b main 2>$null
git -C $main -c user.name=selftest -c user.email=selftest@example.com add -A 2>$null
git -C $main -c user.name=selftest -c user.email=selftest@example.com commit -q -m init 2>$null
git -C $main worktree add -q -b selftest $wt 2>$null

$guard = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "$hooks/trunk-guard.ps1")
function EditJson($file, $cwd) { (@{ tool_name = 'Edit'; cwd = $cwd; tool_input = @{ file_path = $file } } | ConvertTo-Json -Compress) }
Check 'worker edits TRUNK file in a worktree' 2 (Run-Hook 'powershell' $guard (EditJson "$wt\src\Api\Program.cs" $wt))
$posix = '/' + $wt.Substring(0, 1).ToLower() + $wt.Substring(2).Replace('\', '/') + '/src/Api/Program.cs'
Check 'worker edits TRUNK file, Git Bash /c/... path' 2 (Run-Hook 'powershell' $guard (EditJson $posix $wt))
Check 'worker writes new LEAF file in a worktree' 0 (Run-Hook 'powershell' $guard (EditJson "$wt\src\Api\Features\Reports\ReportEndpoints.cs" $wt))
Check 'main session edits TRUNK file' 0 (Run-Hook 'powershell' $guard (EditJson "$main\src\Api\Program.cs" $main))

Write-Host "session-context (SessionStart)"
New-Item -ItemType Directory -Force (Join-Path $main 'docs') | Out-Null
Set-Content (Join-Path $main 'docs/PLAN.md') "- [x] **S0 Skeleton** - TRUNK`n- [ ] **S1 Feature** - LEAF"
$env:CLAUDE_PROJECT_DIR = $main
$ctx = (& $bash "$hooks/session-context.sh" 2>&1) -join "`n"
$code = $LASTEXITCODE
Remove-Item Env:CLAUDE_PROJECT_DIR
$ok = $code -eq 0 -and $ctx -match 'Branch: main' -and $ctx -match 'Next todo: S1 Feature'
Check 'prints branch and next slice' 0 ([pscustomobject]@{ Code = $(if ($ok) { 0 } else { 1 }); Err = $(if ($ok) { '' } else { $ctx }); Verdict = 'CONTEXT' })

git -C $main worktree remove --force $wt 2>$null
Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue

$pass = ($results | Where-Object { $_ }).Count
Write-Host ""
Write-Host "$pass/$($results.Count) passed" -ForegroundColor $(if ($pass -eq $results.Count) { 'Green' } else { 'Red' })
if ($pass -ne $results.Count) { exit 1 }
