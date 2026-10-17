param(
    [switch]$Live,
    [switch]$Resume,
    [string]$RunId,
    [string]$CaseSet = "home-existing",
    [string]$OutputDirectory
)
$ErrorActionPreference = "Stop"
$taskProject = Join-Path $PSScriptRoot "AssistantQualification.csproj"
$taskRepository = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$taskArguments = @("run", "--project", $taskProject, "--")
if ($Live) {
    if ([string]::IsNullOrWhiteSpace($RunId) -or [string]::IsNullOrWhiteSpace($OutputDirectory)) {
        throw "Live qualification requires a run identifier and an output directory."
    }
    $taskArguments += @("--live", "--run-id", $RunId, "--repository", $taskRepository, "--case-set", $CaseSet, "--output", $OutputDirectory)
    if ($Resume) { $taskArguments += "--resume" }
} else {
    $taskArguments += "--list"
}
# Connection strings, key-ring values and API credentials are never arguments, printed, listed or exported.
& dotnet @taskArguments
exit $LASTEXITCODE
