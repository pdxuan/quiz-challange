# PreToolUse guard for Edit/Write: a worker in a linked git worktree may not edit TRUNK files.
# TRUNK globs live in .claude/trunk-paths.txt (written by /plan, committed, so worktrees see it).
# Exit 2 blocks the edit and sends stderr back to the agent; exit 0 allows it.
# The main checkout is never blocked: trunk work happens there, sequentially, under review.
$ErrorActionPreference = 'Stop'
try {
    $in = [Console]::In.ReadToEnd() | ConvertFrom-Json
    $path = $in.tool_input.file_path
    if (-not $path) { $path = $in.tool_input.notebook_path }
    if (-not $path) { exit 0 }
    if ($path -match '^/([a-zA-Z])/(.*)$') { $path = $Matches[1] + ':/' + $Matches[2] }   # Git Bash /c/... form
    if (-not [System.IO.Path]::IsPathRooted($path)) { $path = Join-Path $in.cwd $path }
    $path = [System.IO.Path]::GetFullPath($path)

    # Nearest existing directory of the target (Write may create new folders).
    $dir = Split-Path $path
    while ($dir -and -not (Test-Path -LiteralPath $dir)) { $dir = Split-Path $dir }
    if (-not $dir) { exit 0 }

    $ErrorActionPreference = 'Continue'
    $gitDir = git -C $dir rev-parse --absolute-git-dir 2>$null
    if ($LASTEXITCODE -ne 0) { exit 0 }    # not a git repo
    $commonDir = git -C $dir rev-parse --path-format=absolute --git-common-dir 2>$null
    $top = git -C $dir rev-parse --show-toplevel 2>$null
    $ErrorActionPreference = 'Stop'

    $norm = { param($p) ([System.IO.Path]::GetFullPath($p)).TrimEnd('\', '/').ToLowerInvariant() }
    if ((& $norm $gitDir) -eq (& $norm $commonDir)) { exit 0 }   # main checkout, not a worktree

    $list = Join-Path $top '.claude/trunk-paths.txt'
    if (-not (Test-Path -LiteralPath $list)) { exit 0 }

    $rel = $path.Substring(([System.IO.Path]::GetFullPath($top)).TrimEnd('\', '/').Length).TrimStart('\', '/').Replace('\', '/')
    foreach ($line in Get-Content -LiteralPath $list) {
        $glob = $line.Trim()
        if (-not $glob -or $glob.StartsWith('#')) { continue }
        # glob -> regex: **/ = any folders, ** = anything, * = within one folder, ? = one char
        $rx = [regex]::Escape($glob) -replace '\\\*\\\*/', '(?:.*/)?' -replace '\\\*\\\*', '.*' -replace '\\\*', '[^/]*' -replace '\\\?', '[^/]'
        $target = if ($glob.Contains('/')) { $rel } else { ($rel -split '/')[-1] }
        if ($target -match "^$rx$") {
            [Console]::Error.WriteLine("Blocked by trunk-guard: '$rel' is TRUNK (matches '$glob' in .claude/trunk-paths.txt).")
            [Console]::Error.WriteLine("Workers in a worktree edit LEAF files only. Stop and report the trunk change you need; the main session will make it.")
            exit 2
        }
    }
    exit 0
}
catch {
    # A broken guard must not stall the session; the reviewer and the human still check scope.
    [Console]::Error.WriteLine("trunk-guard error (edit allowed): $($_.Exception.Message)")
    exit 0
}
