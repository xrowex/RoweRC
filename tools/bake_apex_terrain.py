"""Bake collision queries from the preserved FBX; never modifies the FBX or scene."""
import bpy, bmesh, json, pathlib, struct, hashlib, time
from mathutils import Vector

root = pathlib.Path('E:/roweRCtandems')
out = root/'Maps/Support'; out.mkdir(parents=True, exist_ok=True)
evidence = root/'evidence/apex-map-import'
source = pathlib.Path('C:/Users/xrowe/Downloads/APEX_Wangan_Drift_City.fbx')
bpy.ops.wm.read_factory_settings(use_empty=True)
from io_scene_fbx import import_fbx
import_fbx.blen_read_light = lambda *args: bpy.data.lights.new('Ignored light', type='POINT')
bpy.ops.import_scene.fbx(filepath=str(source))
offset = json.loads((evidence/'recovery-result.json').read_text())['offsetCm']
delta = json.loads((evidence/'native-alignment.json').read_text())['deltaCm']
offset = [a+b for a,b in zip(offset,delta)]
ground, walls = bytearray(), bytearray()
counts = {}; skipped = []
for obj in bpy.data.objects:
    if obj.type != 'MESH': continue
    # These are the hidden authoring preview car, not city obstacles.
    if obj.name.startswith('Car_'): continue
    mesh=obj.data
    if len(mesh.polygons)>5000 and obj.name not in ('Plane','Plane.002'):
        mesh=mesh.copy();bm=bmesh.new();bm.from_mesh(mesh)
        bmesh.ops.dissolve_limit(bm,angle_limit=.005,verts=list(bm.verts),edges=list(bm.edges),delimit={'NORMAL'})
        bm.to_mesh(mesh);bm.free()
    mesh.calc_loop_triangles()
    world=[obj.matrix_world@v.co for v in mesh.vertices]
    verts=[Vector((v.x*100+offset[0],-v.y*100+offset[1],v.z*100+offset[2])) for v in world]
    if not verts: continue
    center=[(min(v[i] for v in verts)+max(v[i] for v in verts))/2 for i in range(3)]
    if abs(center[0])>15000 or abs(center[1])>15000 or abs(center[2])>5000:
        skipped.append(obj.name); continue
    ng=nw=0
    for triangle in mesh.loop_triangles:
        a,b,c=[verts[i] for i in triangle.vertices]
        n=(b-a).cross(c-a)
        if n.length_squared<1e-12: continue
        n.normalize()
        # Winding flips under the Blender -> Unreal Y reflection.
        if obj.name in ('Plane','Plane.002') and abs(n.z)>.7:
            ground.extend(struct.pack('<9f',*a,*b,*c)); ng+=1
        elif abs(n.z)<.5 and min(a.z,b.z,c.z)<160 and max(a.z,b.z,c.z)>-15:
            walls.extend(struct.pack('<9f',*a,*b,*c)); nw+=1
    if ng or nw: counts[obj.name]={'ground':ng,'walls':nw}
target=out/'apex-wangan.rrcterrain'
target.write_bytes(struct.pack('<8sII',b'RRCTRN1\0',len(ground)//36,len(walls)//36)+ground+walls)
report={'completed':True,'sourceSha256':hashlib.sha256(source.read_bytes()).hexdigest(),
        'terrainSha256':hashlib.sha256(target.read_bytes()).hexdigest(),'bytes':target.stat().st_size,
        'groundTriangles':len(ground)//36,'wallTriangles':len(walls)//36,'offsetCm':offset,
        'skippedOutliers':skipped,'objects':counts,'path':str(target)}
(evidence/'native-support/terrain-bake.json').write_text(json.dumps(report,indent=2))
print(json.dumps({k:v for k,v in report.items() if k not in ('objects','skippedOutliers')},indent=2),flush=True)
