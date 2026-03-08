param(
    [string]$Mode,
    [string]$StateFile
)

$launchCount = 1
if ($StateFile) {
    if (Test-Path $StateFile) {
        $launchCount = ([int](Get-Content $StateFile)) + 1
    }
    Set-Content -Path $StateFile -Value $launchCount
}

while (($line = [Console]::In.ReadLine()) -ne $null) {
    $request = $line | ConvertFrom-Json

    switch ($Mode) {
        "echo" {
            @{
                id = $request.id
                ok = $true
                result = (@{
                    command = $request.cmd
                    arg0 = if ($request.args.Count -gt 0) { [string]$request.args[0] } else { "" }
                } | ConvertTo-Json -Compress)
            } | ConvertTo-Json -Compress
        }

        "fatal-then-ok" {
            if ($launchCount -eq 1) {
                @{
                    id = $request.id
                    ok = $true
                    result = (@{ error = "Not connected to Tekla Structures" } | ConvertTo-Json -Compress)
                } | ConvertTo-Json -Compress
            }
            else {
                @{
                    id = $request.id
                    ok = $true
                    result = (@{ status = "connected"; launch = $launchCount } | ConvertTo-Json -Compress)
                } | ConvertTo-Json -Compress
            }
        }

        "malformed-then-ok" {
            if ($launchCount -eq 1) {
                "not-json"
            }
            else {
                @{
                    id = $request.id
                    ok = $true
                    result = (@{ status = "recovered"; launch = $launchCount } | ConvertTo-Json -Compress)
                } | ConvertTo-Json -Compress
            }
        }

        "timeout-then-ok" {
            if ($launchCount -eq 1) {
                Start-Sleep -Seconds 2
            }
            else {
                @{
                    id = $request.id
                    ok = $true
                    result = (@{ status = "recovered"; launch = $launchCount } | ConvertTo-Json -Compress)
                } | ConvertTo-Json -Compress
            }
        }

        default {
            @{
                id = $request.id
                ok = $false
                error = "Unknown fake bridge mode: $Mode"
            } | ConvertTo-Json -Compress
        }
    }
}
