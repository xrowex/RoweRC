"""Extract mesh-local collision outlines offline, outside the running game."""
import json,pathlib,traceback,unreal as u
root=pathlib.Path(u.Paths.project_dir());report={'completed':False,'shells':{}}
def hull(points):
    points=sorted(set((round(v.x,2),round(v.y,2)) for v in points))
    def cross(a,b,c):return (b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0])
    lower=[];upper=[]
    for p in points:
        while len(lower)>=2 and cross(lower[-2],lower[-1],p)<=1e-10:lower.pop()
        lower.append(p)
    for p in reversed(points):
        while len(upper)>=2 and cross(upper[-2],upper[-1],p)<=1e-10:upper.pop()
        upper.append(p)
    return lower[:-1]+upper[:-1]
try:
    library=getattr(u,'ProceduralMeshLibrary',None) or getattr(u,'KismetProceduralMeshLibrary',None)
    assert library,[name for name in dir(u) if 'ProceduralMesh' in name]
    for key in ('optional_shell','supra_rc'):
        mesh=u.EditorAssetLibrary.load_asset('/Game/Vehicle/RoweRC/Shells/'+key+'/SM_'+key)
        vertices=[]
        for section in range(mesh.get_num_sections(0)):
            arrays=library.get_section_from_static_mesh(mesh,0,section)
            vertices.extend(arrays[0])
        boundary=hull(vertices)
        report['shells'][key]={'path':mesh.get_path_name(),'hull':boundary,'verticesScannedOffline':len(vertices)}
    report['completed']=True
except Exception:
    report['error']=traceback.format_exc();u.log_error(report['error'])
finally:
    (root/'shell-collision-outlines.json').write_text(json.dumps(report,indent=2)+'\n')
    u.SystemLibrary.quit_editor()
