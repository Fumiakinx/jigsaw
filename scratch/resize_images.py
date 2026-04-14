from PIL import Image
import os

paths = [
    r'c:\Users\nakam\UnityProject\Jigsaw\Assets\Images\PuzzleBase\donuts_1.png',
    r'c:\Users\nakam\UnityProject\Jigsaw\Assets\Images\PuzzleBase\donuts_2.png',
    r'c:\Users\nakam\UnityProject\Jigsaw\Assets\Images\PuzzleBase\donuts_3.png'
]

def resize_image(path, target_width=1920, target_height=1080):
    if not os.path.exists(path):
        print(f"File not found: {path}")
        return
    
    with Image.open(path) as img:
        print(f"Original size of {os.path.basename(path)}: {img.size}")
        
        # Calculate aspect ratios
        target_ratio = target_width / target_height
        img_ratio = img.width / img.height
        
        if img_ratio > target_ratio:
            # Image is wider than target - crop width
            new_width = int(target_ratio * img.height)
            offset = (img.width - new_width) // 2
            img = img.crop((offset, 0, offset + new_width, img.height))
        elif img_ratio < target_ratio:
            # Image is taller than target - crop height
            new_height = int(img.width / target_ratio)
            offset = (img.height - new_height) // 2
            img = img.crop((0, offset, img.width, offset + new_height))
            
        # Resize to final dimensions with high quality
        resized_img = img.resize((target_width, target_height), Image.Resampling.LANCZOS)
        resized_img.save(path, quality=95, optimize=True)
        print(f"Resized {os.path.basename(path)} to {target_width}x{target_height}")

if __name__ == "__main__":
    for p in paths:
        resize_image(p)
