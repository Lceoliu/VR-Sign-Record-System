# Third-party assets

This folder contains the curated PC-rendering assets used by the realtime
translation demo. They are intentionally isolated from the Quest recording
scene and its mobile render pipeline.

## Poly Haven

Downloaded on 2026-08-20 from [Poly Haven](https://polyhaven.com/). Poly Haven
publishes its HDRIs, textures, and models under the
[CC0 license](https://polyhaven.com/license), so these files may be used,
modified, and redistributed with the Unity project.

| Local asset | Source | Imported files |
| --- | --- | --- |
| `PolyHaven/Textures/laminate_floor_02` | [Laminate Floor 02](https://polyhaven.com/a/laminate_floor_02) | 4K JPEG diffuse, OpenGL normal, roughness, AO |
| `PolyHaven/Textures/beige_wall_001` | [Beige Wall 001](https://polyhaven.com/a/beige_wall_001) | 4K JPEG diffuse, OpenGL normal, roughness, AO |
| `PolyHaven/Textures/oak_veneer_01` | [Oak Veneer 01](https://polyhaven.com/a/oak_veneer_01) | 4K JPEG diffuse, OpenGL normal, roughness, AO |
| `PolyHaven/Textures/poly_wool_herringbone` | [Poly Wool Herringbone](https://polyhaven.com/a/poly_wool_herringbone) | 4K JPEG diffuse, OpenGL normal, roughness, AO |
| `PolyHaven/Textures/curly_teddy_natural` | [Curly Teddy Natural](https://polyhaven.com/a/curly_teddy_natural) | 4K JPEG diffuse, OpenGL normal, roughness, AO |
| `PolyHaven/Models/potted_plant_02` | [Potted Plant 02](https://polyhaven.com/a/potted_plant_02) | 4K FBX, 4K JPEG material maps, PNG leaf alpha |
| `PolyHaven/HDRI/canary_wharf` | [Canary Wharf](https://polyhaven.com/a/canary_wharf) | 4K HDR panorama |
| `PolyHaven/HDRI/modern_buildings_2` | [Modern Buildings 2](https://polyhaven.com/a/modern_buildings_2) | 4K HDR panorama; active PC demo exterior |

The source roughness maps remain beside the imported materials. Unity-ready
metallic/smoothness masks are generated from them by the demo upgrade command;
the original source maps are retained for future HDRP or DCC rendering.
