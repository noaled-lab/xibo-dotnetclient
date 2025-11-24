# PNG to ICO Converter Script
Add-Type -AssemblyName System.Drawing

$pngPath = "D:\xibo-cms\xibo-dotnetclient\Resources\logo.png"
$icoPath = "D:\xibo-cms\xibo-dotnetclient\new-icon.ico"

if (Test-Path $pngPath) {
    try {
        # Load the PNG image
        $bitmap = New-Object System.Drawing.Bitmap($pngPath)
        
        # Create ICO file
        # For ICO, we need multiple sizes. Let's create common sizes
        $sizes = @(16, 32, 48, 64, 128, 256)
        $images = New-Object System.Collections.ArrayList
        
        foreach ($size in $sizes) {
            $resized = New-Object System.Drawing.Bitmap($bitmap, $size, $size)
            [void]$images.Add($resized)
        }
        
        # Save as ICO (simplified - using PNG as base)
        # Note: Full ICO format requires more complex handling
        # For now, we'll create a simple ICO using the largest size
        $largest = $images[$images.Count - 1]
        $largest.Save($icoPath, [System.Drawing.Imaging.ImageFormat]::Icon)
        
        Write-Host "ICO file created successfully: $icoPath"
        
        # Clean up
        $bitmap.Dispose()
        foreach ($img in $images) {
            $img.Dispose()
        }
    }
    catch {
        Write-Host "Error converting PNG to ICO: $_"
        # Fallback: Copy PNG as icon (may not work perfectly)
        Write-Host "Trying alternative method..."
        Copy-Item $pngPath -Destination $icoPath -Force
    }
} else {
    Write-Host "PNG file not found: $pngPath"
}

