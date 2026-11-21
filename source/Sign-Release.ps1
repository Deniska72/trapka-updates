param(
    [Parameter(Mandatory=$true)][string]$FilePath,
    [Parameter(Mandatory=$true)][string]$DownloadUrl,
    [Parameter(Mandatory=$true)][string]$PrivateKeyPath,
    [Parameter(Mandatory=$true)][string]$OutPath
)
$ErrorActionPreference = 'Stop'
$downloadUri = [Uri]$DownloadUrl
if (-not $downloadUri.IsAbsoluteUri -or $downloadUri.Scheme -ne 'https') { throw 'HTTPS URL required' }
$releaseFile = Get-Item -LiteralPath $FilePath
$releaseVersion = [Reflection.AssemblyName]::GetAssemblyName($releaseFile.FullName).Version.ToString()
$releaseHash = (Get-FileHash -LiteralPath $releaseFile.FullName -Algorithm SHA256).Hash
$releaseSize = $releaseFile.Length.ToString([Globalization.CultureInfo]::InvariantCulture)
$canonical = "TrapkaUpdate-v1`n$releaseVersion`n$DownloadUrl`n$releaseSize`n$releaseHash"
$rsa = [Security.Cryptography.RSACryptoServiceProvider]::new([Security.Cryptography.CspParameters]::new(24))
$rsa.PersistKeyInCsp = $false
try {
    $rsa.FromXmlString([IO.File]::ReadAllText((Resolve-Path -LiteralPath $PrivateKeyPath)))
    $signature = [Convert]::ToBase64String($rsa.SignData([Text.Encoding]::UTF8.GetBytes($canonical), 'SHA256'))
} finally { $rsa.Dispose() }
$xml = [Xml.XmlDocument]::new()
$root = $xml.CreateElement('TrapkaUpdate')
$xml.AppendChild($root) | Out-Null
foreach ($item in @(@('Version',$releaseVersion),@('Url',$DownloadUrl),@('Size',$releaseSize),@('Sha256',$releaseHash),@('Signature',$signature))) {
    $element = $xml.CreateElement($item[0]); $element.InnerText = $item[1]; $root.AppendChild($element) | Out-Null
}
$settings = [Xml.XmlWriterSettings]::new(); $settings.Indent = $true; $settings.Encoding = [Text.UTF8Encoding]::new($false)
$writer = [Xml.XmlWriter]::Create([IO.Path]::GetFullPath($OutPath), $settings)
try { $xml.WriteTo($writer) } finally { $writer.Dispose() }
Write-Output "Signed release $releaseVersion. Manifest: $OutPath"
