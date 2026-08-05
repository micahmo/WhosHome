# Generates the PWA and Unraid icons from the same shapes as WhosHome.Web/public/favicon.svg.
# Kept as a script rather than committing only the output so the icons can be regenerated if
# the mark changes. Run from anywhere:
#
#   pwsh tools/generate-icons.ps1
#
# Chrome will not offer to install a web app unless the manifest has a valid icon of at least
# 192px, so these are load bearing rather than decorative.

Add-Type -AssemblyName System.Drawing

$outputDirectory = Join-Path $PSScriptRoot '..\WhosHome.Web\public'
$outputDirectory = [System.IO.Path]::GetFullPath($outputDirectory)

$background = [System.Drawing.ColorTranslator]::FromHtml('#121214')
$foreground = [System.Drawing.ColorTranslator]::FromHtml('#f2f2f4')
$accent = [System.Drawing.ColorTranslator]::FromHtml('#48c46b')

# The house outline from the SVG, in its original 32 unit coordinate space.
$housePoints = @(
    @(16.0, 7.0), @(6.0, 15.4), @(8.7, 15.4), @(8.7, 25.0), @(13.7, 25.0),
    @(13.7, 19.2), @(18.3, 19.2), @(18.3, 25.0), @(23.3, 25.0), @(23.3, 15.4), @(26.0, 15.4)
)
$dotCenter = @(16.0, 12.8)
$dotRadius = 2.0

function New-Icon {
    param(
        [string] $Path,
        [int] $Size,
        # Maskable icons are cropped to a circle by the launcher, so the mark shrinks into the
        # safe zone and the background bleeds to the edges with no rounded corners.
        [switch] $Maskable
    )

    $bitmap = New-Object System.Drawing.Bitmap($Size, $Size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

    $backgroundBrush = New-Object System.Drawing.SolidBrush($background)
    $foregroundBrush = New-Object System.Drawing.SolidBrush($foreground)
    $accentBrush = New-Object System.Drawing.SolidBrush($accent)

    if ($Maskable) {
        $graphics.FillRectangle($backgroundBrush, 0, 0, $Size, $Size)
        $scale = ($Size / 32.0) * 0.62
    }
    else {
        $radius = $Size * (7.0 / 32.0)
        $rounded = New-Object System.Drawing.Drawing2D.GraphicsPath
        $diameter = $radius * 2
        $rounded.AddArc(0, 0, $diameter, $diameter, 180, 90)
        $rounded.AddArc($Size - $diameter, 0, $diameter, $diameter, 270, 90)
        $rounded.AddArc($Size - $diameter, $Size - $diameter, $diameter, $diameter, 0, 90)
        $rounded.AddArc(0, $Size - $diameter, $diameter, $diameter, 90, 90)
        $rounded.CloseFigure()
        $graphics.FillPath($backgroundBrush, $rounded)
        $rounded.Dispose()
        $scale = $Size / 32.0
    }

    # Centre the mark. The glyph occupies roughly x 6..26 and y 7..25 of the 32 unit box.
    $offsetX = ($Size - (32.0 * $scale)) / 2.0
    $offsetY = ($Size - (32.0 * $scale)) / 2.0

    $points = foreach ($point in $housePoints) {
        New-Object System.Drawing.PointF(
            [float]($offsetX + $point[0] * $scale),
            [float]($offsetY + $point[1] * $scale))
    }
    $graphics.FillPolygon($foregroundBrush, [System.Drawing.PointF[]]$points)

    $dotDiameter = $dotRadius * 2 * $scale
    $graphics.FillEllipse(
        $accentBrush,
        [float]($offsetX + ($dotCenter[0] - $dotRadius) * $scale),
        [float]($offsetY + ($dotCenter[1] - $dotRadius) * $scale),
        [float]$dotDiameter,
        [float]$dotDiameter)

    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)

    $accentBrush.Dispose()
    $foregroundBrush.Dispose()
    $backgroundBrush.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()

    Write-Output ("{0}  {1}x{1}" -f (Split-Path $Path -Leaf), $Size)
}

New-Icon -Path (Join-Path $outputDirectory 'icon-192.png') -Size 192
New-Icon -Path (Join-Path $outputDirectory 'icon-512.png') -Size 512
New-Icon -Path (Join-Path $outputDirectory 'icon-maskable-512.png') -Size 512 -Maskable
