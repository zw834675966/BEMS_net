param(
    [Parameter(Mandatory = $true)]
    [string]$Host,

    [Parameter(Mandatory = $false)]
    [int]$Port = 5432,

    [Parameter(Mandatory = $true)]
    [string]$Database,

    [Parameter(Mandatory = $true)]
    [string]$Username,

    [Parameter(Mandatory = $true)]
    [string]$Password
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command psql -ErrorAction SilentlyContinue)) {
    throw "psql was not found in PATH. Please install PostgreSQL client tools first."
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$initSql = Join-Path $scriptDir "init\\001_init.sql"
$verifySql = Join-Path $scriptDir "verify.sql"

$env:PGPASSWORD = $Password

try {
    psql -h $Host -p $Port -U $Username -d $Database -f $initSql
    psql -h $Host -p $Port -U $Username -d $Database -f $verifySql
}
finally {
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
}

Write-Host "Database initialization and verification completed."
