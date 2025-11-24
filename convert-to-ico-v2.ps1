# Better PNG to ICO Converter using .NET
Add-Type -AssemblyName System.Drawing

$pngPath = "D:\xibo-cms\xibo-dotnetclient\Resources\logo.png"
$icoPath = "D:\xibo-cms\xibo-dotnetclient\new-icon.ico"

if (Test-Path $pngPath) {
    try {
        # Load the PNG image
        $bitmap = New-Object System.Drawing.Bitmap($pngPath)
        
        # Create a MemoryStream for ICO
        $ms = New-Object System.IO.MemoryStream
        
        # ICO format requires specific structure
        # For now, let's create a simple ICO by saving as PNG and renaming
        # This is a workaround - proper ICO needs IconLibrary or similar
        
        # Create multiple sizes for ICO
        $sizes = @(16, 32, 48, 64, 128, 256)
        $iconImages = @()
        
        foreach ($size in $sizes) {
            $resized = New-Object System.Drawing.Bitmap($bitmap, $size, $size)
            $iconImages += $resized
        }
        
        # Save as multi-size ICO
        # Note: This is a simplified approach. For proper ICO, we need IconLibrary
        # For now, save the largest size as a temporary ICO
        $largest = $iconImages[$iconImages.Count - 1]
        
        # Try to save as ICO - if this doesn't work, we'll use a workaround
        try {
            # Create proper ICO format
            $icoStream = New-Object System.IO.MemoryStream
            $icoWriter = New-Object System.IO.BinaryWriter($icoStream)
            
            # ICO header
            $icoWriter.Write([UInt16]0)  # Reserved
            $icoWriter.Write([UInt16]1)   # Type (1 = ICO)
            $icoWriter.Write([UInt16]$iconImages.Count)  # Number of images
            
            # Write directory entries
            $offset = 6 + ($iconImages.Count * 16)
            foreach ($img in $iconImages) {
                $width = if ($img.Width -eq 256) { 0 } else { $img.Width }
                $height = if ($img.Height -eq 256) { 0 } else { $img.Height }
                
                $icoWriter.Write([Byte]$width)
                $icoWriter.Write([Byte]$height)
                $icoWriter.Write([Byte]0)  # Color palette
                $icoWriter.Write([Byte]0)  # Reserved
                $icoWriter.Write([UInt16]1)  # Color planes
                $icoWriter.Write([UInt16]32)  # Bits per pixel
                
                # Calculate image data size
                $imgStream = New-Object System.IO.MemoryStream
                $img.Save($imgStream, [System.Drawing.Imaging.ImageFormat]::Png)
                $imgData = $imgStream.ToArray()
                $imgStream.Dispose()
                
                $icoWriter.Write([UInt32]$imgData.Length)
                $icoWriter.Write([UInt32]$offset)
                $offset += $imgData.Length
            }
            
            # Write image data
            foreach ($img in $iconImages) {
                $imgStream = New-Object System.IO.MemoryStream
                $img.Save($imgStream, [System.Drawing.Imaging.ImageFormat]::Png)
                $imgData = $imgStream.ToArray()
                $icoWriter.Write($imgData)
                $imgStream.Dispose()
            }
            
            # Save ICO file
            [System.IO.File]::WriteAllBytes($icoPath, $icoStream.ToArray())
            $icoStream.Dispose()
            $icoWriter.Close()
            
            Write-Host "ICO file created successfully: $icoPath"
        }
        catch {
            Write-Host "Error creating ICO: $_"
            Write-Host "Using fallback: copying PNG as icon"
            # Fallback: just copy the PNG (may not work in all cases)
            Copy-Item $pngPath -Destination $icoPath -Force
        }
        
        # Clean up
        $bitmap.Dispose()
        foreach ($img in $iconImages) {
            $img.Dispose()
        }
    }
    catch {
        Write-Host "Error: $_"
        Write-Host "Using fallback: copying PNG"
        Copy-Item $pngPath -Destination $icoPath -Force
    }
} else {
    Write-Host "PNG file not found: $pngPath"
}

