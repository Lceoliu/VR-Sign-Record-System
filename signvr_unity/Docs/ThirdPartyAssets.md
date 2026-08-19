# Third-party asset notes

The co-op lift scene uses the FBX assets under `Assets/Resources`:

- `stone/source/Stein.fbx`
- `key/source/key.fbx`
- `Thermos2/Thermos2.fbx`
- `elevator-button-lift/source/BUTON.fbx`
- `direction-arrow/source/3D RightArrow.fbx`

The repository currently contains no source URLs, author credits, or license
files for these assets. Treat them as local development assets only. Record
the original download page and license for each model before redistribution
or release.

The scene generator uses primitive colliders around these models. It does not
use their render meshes as dynamic MeshColliders. The Thermos model is notably
heavy for Quest and should be replaced with a reduced mesh before production.
