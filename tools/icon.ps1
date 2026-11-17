Add-Type -AssemblyName System.Drawing
$icon = Join-Path $PSScriptRoot '..\src\JagexManager\icon.ico'
$preview = Join-Path ([IO.Path]::GetTempPath()) 'JagexManager-icon.png'

$palette = @{
    '#' = '#1A1410'
    'k' = '#7A4200'
    'd' = '#B06A00'
    'o' = '#DC8A00'
    'l' = '#F7B733'
    'h' = '#FFE488'
}

$grids = @{
    16 = @(
        '........######..'
        '.......#llllll#.'
        '......#hhhhhlll#'
        '......#kloooodk#'
        '....####kkkkkkl#'
        '..##hhhl##hhlld#'
        '.#hhooooll#oodk#'
        '.#hooooool#kkkl#'
        '#hooo#ooooo#lld#'
        '#hooo##oooo#odk#'
        '#hooo###ood#kkl#'
        '#looo##oood#lld#'
        '.#loo#oood#ood#.'
        '.#looooodd####..'
        '..##oodd##......'
        '....####........'
    )
    20 = @(
        '...........######...'
        '.........##llllll##.'
        '........#hhllllllll#'
        '........#llhhhhlldd#'
        '........#lllooooodd#'
        '........#kkloooookk#'
        '.....####hhkkkkkkll#'
        '...##hhhl##hhhhlldd#'
        '..#hhooooll#ooooodd#'
        '.#hooooooool#ooookk#'
        '.#hooooooooo#kkkkll#'
        '#hooookoooooo#hlldd#'
        '#hooookkooooo#ooodd#'
        '#hooookkkoood#oookk#'
        '#looookkloood#kkkll#'
        '.#loookloood#hhlldd#'
        '.#looooooood#oooodd#'
        '..#looooodd#ooooo##.'
        '...##oodd########...'
        '.....####...........'
    )
    24 = @(
        '.............########...'
        '...........##llllllll##.'
        '..........#hhllllllllll#'
        '..........#llhhhhhhlldd#'
        '..........#lllooooooddd#'
        '..........#kkloooooodkk#'
        '..........#hhkkkkkkkkll#'
        '......#####llhhhhhhlldd#'
        '....##hhhll##looooooddd#'
        '...#hhhhhllll#oooooodkk#'
        '..#hhhdddddlll#kkkkkkll#'
        '.#hhhdooooodlll#hhhlldd#'
        '.#hhdookooooolo#ooooddd#'
        '#hhdoookkooooooo#ooodkk#'
        '#hhdoookkkoooloo#kkkkll#'
        '#hhdoookkkkooloo#hhlldd#'
        '#lldoookkkloolod#oooddd#'
        '#lldoookkloooldd#ooodkk#'
        '.#lloookloooldd#kkkkkll#'
        '.#llooooooolldd#hhhlldd#'
        '..#loolllllddd#oooooddd#'
        '...#ooooddddd#ooooood##.'
        '....##ooddd##########...'
        '......#####.............'
    )
    32 = @(
        '.................##########.....'
        '...............##llllllllll##...'
        '..............#llhhhhllllllll#..'
        '.............#hlllllllllllllll#.'
        '.............#lhhlllllllllllld#.'
        '.............#lllhhhhhhhlllddd#.'
        '.............#lllloooooooodddd#.'
        '.............#kllloooooooodddk#.'
        '..............#kkloooooooodkkll#'
        '..............#lhkkkkkkkkkkllld#'
        '........#######lllhhhhhhhlllddd#'
        '......##hhhlll##llloooooooodddd#'
        '....##hhhhhlllll##loooooooodddk#'
        '...#hhhhddddddllll#oooooooodkk#.'
        '..#hhhddooooooddlll#kkkkkkkkld#.'
        '..#hhddooooooooooll#hhhhlllddd#.'
        '.#hhddoookoooooooolo#ooooodddd#.'
        '.#hhdooookkooooooooo#ooooodddk#.'
        '#hhdoooookkkooooooloo#oooodkk#..'
        '#hhdoooookkkkoooooloo#kkkkkld#..'
        '#hhdoooookkkkkooooloo#hlllddd#..'
        '#lldoooookkkkloooolod#ooodddd#..'
        '#lldoooookkkloooooldd#ooodddk#..'
        '#lldoooookklooooooldd#ooodkkll#.'
        '.#lldooooklooooooldd#kkkkkllld#.'
        '.#lloooooooooooolldd#hhhlllddd#.'
        '..#lloooooooooolldd#oooooodddd#.'
        '..#loooooooooollddd#ooooooddd#..'
        '...#oooolllllldddd#oooooood##...'
        '....##oooodddddd###########.....'
        '......##oodddd##................'
        '........######..................'
    )
}

$sizes = @{ 16 = 16; 20 = 20; 24 = 24; 32 = 32; 40 = 20; 48 = 24; 64 = 32; 128 = 32; 256 = 32 }
$order = $sizes.Keys | Sort-Object

function color($hex) { [Drawing.ColorTranslator]::FromHtml($hex) }

function paint($n) {
    $rows = $grids[$n]
    if ($rows.Count -ne $n) { throw "grid $n has $($rows.Count) rows" }
    $bmp = New-Object Drawing.Bitmap $n, $n, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    for ($y = 0; $y -lt $n; $y++) {
        if ($rows[$y].Length -ne $n) { throw "grid $n row $y is $($rows[$y].Length) wide" }
        for ($x = 0; $x -lt $n; $x++) {
            $key = [string]$rows[$y][$x]
            if ($key -eq '.') { continue }
            if (-not $palette.ContainsKey($key)) { throw "grid $n row $y has unknown colour '$key'" }
            $bmp.SetPixel($x, $y, (color $palette[$key]))
        }
    }
    $bmp
}

function scale($src, $size) {
    $bmp = New-Object Drawing.Bitmap $size, $size, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $gfx = [Drawing.Graphics]::FromImage($bmp)
    $gfx.InterpolationMode = 'NearestNeighbor'
    $gfx.PixelOffsetMode = 'Half'
    $gfx.DrawImage($src, 0, 0, $size, $size)
    $gfx.Dispose()
    $bmp
}

function png($bmp) {
    $ms = New-Object IO.MemoryStream
    $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    , $ms.ToArray()
}

function dib($bmp) {
    $n = $bmp.Width
    $ms = New-Object IO.MemoryStream
    $w = New-Object IO.BinaryWriter $ms
    $w.Write([int]40); $w.Write([int]$n); $w.Write([int]($n * 2))
    $w.Write([int16]1); $w.Write([int16]32)
    $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)
    for ($y = $n - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $n; $x++) { $w.Write([int]$bmp.GetPixel($x, $y).ToArgb()) }
    }
    $stride = [int][Math]::Ceiling($n / 32) * 4
    for ($y = $n - 1; $y -ge 0; $y--) {
        $row = New-Object byte[] $stride
        for ($x = 0; $x -lt $n; $x++) {
            if ($bmp.GetPixel($x, $y).A -eq 0) { $row[$x -shr 3] = $row[$x -shr 3] -bor (0x80 -shr ($x -band 7)) }
        }
        $w.Write($row)
    }
    , $ms.ToArray()
}

function ico($images, $path) {
    $ms = New-Object IO.MemoryStream
    $w = New-Object IO.BinaryWriter $ms
    $w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$images.Count)
    $offset = 6 + 16 * $images.Count
    $blobs = foreach ($size in $order) {
        $blob = if ($size -eq 256) { png $images[$size] } else { dib $images[$size] }
        $w.Write([byte]($size % 256)); $w.Write([byte]($size % 256)); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]$blob.Length); $w.Write([int]$offset)
        $offset += $blob.Length
        , $blob
    }
    foreach ($blob in $blobs) { $w.Write($blob) }
    [IO.File]::WriteAllBytes($path, $ms.ToArray())
}

function sheet($images, $path) {
    $gap = 16; $zoom = 8; $panel = $gap * 3 + 256 * 2
    $bmp = New-Object Drawing.Bitmap 880, ($panel * 2)
    $gfx = [Drawing.Graphics]::FromImage($bmp)
    $gfx.InterpolationMode = 'NearestNeighbor'
    $gfx.PixelOffsetMode = 'Half'
    $top = 0
    foreach ($hex in '#1E1E1E', '#F3F3F3') {
        $gfx.FillRectangle((New-Object Drawing.SolidBrush (color $hex)), 0, $top, $bmp.Width, $panel)
        $x = $gap
        foreach ($size in $order) {
            $gfx.DrawImage($images[$size], $x, $top + $gap + 256 - $size, $size, $size)
            $x += $size + $gap
        }
        $x = $gap
        foreach ($size in $grids.Keys | Sort-Object) {
            $big = $size * $zoom
            $gfx.DrawImage($images[$size], $x, $top + $gap * 2 + 256 * 2 - $big, $big, $big)
            $x += $big + $gap
        }
        $top += $panel
    }
    $gfx.Dispose()
    $bmp.Save($path, [Drawing.Imaging.ImageFormat]::Png)
}

$images = @{}
foreach ($size in $order) { $images[$size] = scale (paint $sizes[$size]) $size }

ico $images $icon
sheet $images $preview
"Wrote $([IO.Path]::GetFullPath($icon)) ($($order -join ', ')) and a preview at $preview"
