#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "terrain_core.h"
#include <filesystem>
#include <chrono>
#include <iomanip>
#include <memory>
#include <sstream>
#include <string>

bool RoweRCVerifySupportedGame();
namespace {
using terrain::V;
constexpr uintptr_t TickRva=0x0432f140,PoseRva=0x03624810;
constexpr size_t TickSlot=163,TableCount=279;
constexpr char Level[]="/Game/RoweRCMaps/apex_wangan/Tandyz_Overcast_With_APEX_Repaired";
using Tick=void(__fastcall*)(void*,float);
using SetPose=bool(__fastcall*)(void*,const V*,const V*,bool,void*,uint8_t);
using SetCamera=void(__fastcall*)(void*,const V*,const V*,bool,void*,uint8_t);
struct Context {
 void* pawn=nullptr;uintptr_t* original=nullptr;std::array<uintptr_t,TableCount+1> table{};
 terrain::Terrain data;bool loaded=false,grounded=false;double height=0,lastTime=-1;
 uint64_t frames=0,contacts=0,boundaryFrames=0;double elapsed=0,maxMs=0;V normal{0,0,1};
};
Context context;
std::filesystem::path bridge(){wchar_t p[32768]{};GetEnvironmentVariableW(L"LOCALAPPDATA",p,32768);return std::filesystem::path(p)/L"RoweRC"/L"bridge";}
void write(const wchar_t* name,const std::string& value){std::ofstream f(bridge()/name,std::ios::binary);f<<value;}
double& d(void* p,size_t at){return *reinterpret_cast<double*>(static_cast<unsigned char*>(p)+at);}
uint8_t& b(void* p,size_t at){return *(static_cast<unsigned char*>(p)+at);}
V position(void* p){return{d(p,0x7760)*100,d(p,0x7758)*100,0};}
V velocity(void* p){double yaw=d(p,0x7768),u=d(p,0x7770),v=d(p,0x7778);return{std::cos(yaw)*u-std::sin(yaw)*v,std::sin(yaw)*u+std::cos(yaw)*v,0};}
void putVelocity(void* p,V v){double yaw=d(p,0x7768);d(p,0x7770)=std::cos(yaw)*v.x+std::sin(yaw)*v.y;d(p,0x7778)=-std::sin(yaw)*v.x+std::cos(yaw)*v.y;}
void unbind(){if(context.pawn){auto slot=reinterpret_cast<uintptr_t**>(context.pawn);uintptr_t* actual=nullptr;SIZE_T got=0;
 if(ReadProcessMemory(GetCurrentProcess(),slot,&actual,sizeof(actual),&got)&&got==sizeof(actual)&&actual==context.table.data()+1)*slot=context.original;
 context.pawn=nullptr;context.original=nullptr;}context.grounded=false;}
bool otherRecovery(void* p){
 // Exactly the non-position checks from the verified recovery function.
 return std::abs(d(p,0x7860+0x58))>25*terrain::pi/180 || std::abs(d(p,0x7860+0x48))>25*terrain::pi/180 || b(p,0x7860+0x2a8)!=0;
}
void __fastcall terrainTick(void* pawn,float seconds){
 auto started=std::chrono::steady_clock::now();auto& c=context;
 if(pawn!=c.pawn){reinterpret_cast<Tick>(GetModuleHandleW(nullptr)?reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr))+TickRva:0)(pawn,seconds);return;}
 auto original=reinterpret_cast<Tick>(c.original[TickSlot]);
 const auto before=position(pawn);const bool driving=b(pawn,0x4c0)!=0;const uint8_t reported=b(pawn,0x94e6);
 // Suppress the fixed arena check only while this map's adapter owns recovery.
 // Other native input/menu pauses are respected; never force driving back on.
 if(driving&&!reported)b(pawn,0x94e6)=1;
 original(pawn,seconds);
 if(driving&&!reported)b(pawn,0x94e6)=reported;
 const double dt=std::clamp(double(seconds),0.,.05);V pos=position(pawn);const V nativePosition=pos;double yaw=d(pawn,0x7768);
 bool reset=d(pawn,0x7750)<c.lastTime || std::hypot(pos.x-before.x,pos.y-before.y)>300;
 if(reset)c.grounded=false;c.lastTime=d(pawn,0x7750);
 auto surface=c.data.sample(pos.x,pos.y,c.grounded?c.height:0, c.grounded?3+std::hypot(pos.x-before.x,pos.y-before.y)*.5:3);
 if(surface.found){
   auto oldHeight=c.grounded?c.height:surface.z;auto normal=surface.normal;
   // Sample a wheelbase-sized plane instead of amplifying tiny triangle seams.
   double fx=std::cos(yaw),fy=std::sin(yaw);std::array<terrain::Surface,4> wheels;
   wheels[0]=c.data.sample(pos.x+fx*15,pos.y+fy*15,surface.z,12);
   wheels[1]=c.data.sample(pos.x-fx*10,pos.y-fy*10,surface.z,12);
   wheels[2]=c.data.sample(pos.x-fy*9.5,pos.y+fx*9.5,surface.z,12);
   wheels[3]=c.data.sample(pos.x+fy*9.5,pos.y-fx*9.5,surface.z,12);
   if(std::all_of(wheels.begin(),wheels.end(),[](auto& s){return s.found;})){
     double along=(wheels[0].z-wheels[1].z)/25,across=(wheels[2].z-wheels[3].z)/19;
     normal={-along*fx+across*fy,-along*fy-across*fx,1};normal=normal*(1/terrain::length(normal));
   }
   V v=velocity(pawn);V from{before.x,before.y,oldHeight};pos.z=surface.z;
   if(!reset){auto hit=c.data.move(from,pos,v,yaw);if(hit.hit){++c.contacts;pos.x=hit.position.x;pos.y=hit.position.y;v=hit.velocity;d(pawn,0x7780)*=.85;
       auto corrected=c.data.sample(pos.x,pos.y,surface.z,3);if(corrected.found){pos.z=corrected.z;normal=corrected.normal;}
     }}
   d(pawn,0x7760)=pos.x/100;d(pawn,0x7758)=pos.y/100;
   if(driving&&b(pawn,0x4c0)){v.x+=9.80665*normal.z*normal.x*dt;v.y+=9.80665*normal.z*normal.y*dt;putVelocity(pawn,v);}
   c.height=pos.z;c.normal=normal;c.grounded=true;
 }else{
   // Do not fall through a hole or snap to another deck: retain the last support
   // and stop planar motion until reset. This adapter supplies grounded driving.
   if(c.grounded){d(pawn,0x7760)=before.x/100;d(pawn,0x7758)=before.y/100;pos={before.x,before.y,c.height};putVelocity(pawn,{});}
   else pos.z=0;
 }
 if(driving&&!reported&&otherRecovery(pawn)){
   // Let the stock recovery path handle genuine attitude/lift on the next tick.
   // A native pause is still required immediately to avoid an uncontrolled frame.
   reinterpret_cast<void(__fastcall*)(void*)>(reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr))+0x04327b00)(pawn);
   b(pawn,0x94e6)=1;putVelocity(pawn,{});write(L"terrain-recovery.txt","Grounded attitude or wheel-lift recovery required; reset the car.");
 }
 if(std::abs(pos.x)>1800||std::abs(pos.y)>1800)++c.boundaryFrames;
 // Native Camera has absolute location/rotation: moving the pawn cannot carry it.
 // Translate the native view by the road height, preserving its chosen mode,
 // orbit, zoom and world orientation rather than banking the camera with the car.
 void* camera=*reinterpret_cast<void**>(static_cast<unsigned char*>(pawn)+0xb28);
 V cameraPosition{d(camera,0x210),d(camera,0x218),d(camera,0x220)};
 const double nativeCameraZ=cameraPosition.z;
 V cameraRotation{d(camera,0x160),d(camera,0x168),d(camera,0x170)};
 const auto angles=terrain::attitude(c.normal,yaw);V rotation{angles[0],yaw*180/terrain::pi,angles[1]};
 auto base=reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));reinterpret_cast<SetPose>(base+PoseRva)(pawn,&pos,&rotation,false,nullptr,0);
 cameraPosition.z+=pos.z;
 cameraPosition.x+=pos.x-nativePosition.x;cameraPosition.y+=pos.y-nativePosition.y;
 reinterpret_cast<SetCamera>(base+0x038e7cd0)(camera,&cameraPosition,&cameraRotation,false,nullptr,0);
 ++c.frames;c.elapsed+=dt;c.maxMs=std::max(c.maxMs,std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-started).count());
 if(c.elapsed>=1){std::ostringstream out;out<<std::setprecision(12)<<"RRCTERRAIN1\t"<<c.frames<<'\t'<<int(b(pawn,0x4c0))<<'\t'<<pos.x<<'\t'<<pos.y<<'\t'<<pos.z<<'\t'<<rotation.x<<'\t'<<rotation.z<<'\t'<<c.contacts<<'\t'<<c.boundaryFrames<<'\t'<<c.maxMs<<'\t'<<c.grounded<<'\t'<<nativeCameraZ<<'\t'<<d(camera,0x220)<<'\n';write(L"terrain-state.tsv",out.str());c.elapsed=0;c.maxMs=0;}
}
void bind(){
 std::ifstream f(bridge()/L"terrain-context.tsv");std::string tag,level;uintptr_t address=0;double x=0,y=0;
 f>>tag>>std::hex>>address>>std::dec>>x>>y>>level;
 if(!f||tag!="RRCTERRAIN1"||!address||level!=Level)throw std::runtime_error("Invalid terrain context");
 void* pawn=reinterpret_cast<void*>(address);if(context.pawn==pawn)return;
 if(!RoweRCVerifySupportedGame())throw std::runtime_error("Terrain adapter: unsupported game executable");
 auto table=*reinterpret_cast<uintptr_t**>(pawn);auto base=reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
 if(table[TickSlot]!=base+TickRva||std::abs(d(pawn,0x7760)*100-x)>.5||std::abs(d(pawn,0x7758)*100-y)>.5)throw std::runtime_error("Terrain adapter: pawn layout mismatch");
 if(!context.loaded){HMODULE own=nullptr;GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,reinterpret_cast<LPCWSTR>(&bind),&own);wchar_t file[32768]{};GetModuleFileNameW(own,file,32768);context.data.load(std::filesystem::path(file).parent_path()/L"Terrain"/L"apex-wangan.rrcterrain");context.loaded=true;}
 unbind();context.pawn=pawn;context.original=table;std::copy_n(table-1,TableCount+1,context.table.begin());context.table[TickSlot+1]=reinterpret_cast<uintptr_t>(&terrainTick);*reinterpret_cast<uintptr_t**>(pawn)=context.table.data()+1;
 context.frames=context.contacts=context.boundaryFrames=0;context.elapsed=0;context.lastTime=-1;context.normal={0,0,1};write(L"terrain-error.txt","");write(L"terrain-recovery.txt","");write(L"terrain-bind.txt","Active: APEX city grounded terrain adapter");
}
void probe(){
 // Local regression fixture, only while disconnected and already in Drive view.
 if(!context.pawn||!b(context.pawn,0x4c0))throw std::runtime_error("Enter city Drive view before a terrain probe");
 std::ifstream stateFile(bridge()/L"ui-state.tsv");std::string state((std::istreambuf_iterator<char>(stateFile)),{});
 if(state.find("\nrole\tidle\n")==std::string::npos)throw std::runtime_error("Terrain probe requires a disconnected test session");
 std::ifstream f(bridge()/L"terrain-probe-request.tsv");std::string id;double x,y,z,yaw,vx,vy;
 f>>id>>x>>y>>z>>yaw>>vx>>vy;
 if(!f||id.size()>64)throw std::runtime_error("Invalid terrain probe");
 for(auto value:{x,y,z,yaw,vx,vy})if(!std::isfinite(value))throw std::runtime_error("Invalid terrain probe number");
 if(std::abs(x)>10000||std::abs(y)>10000||std::abs(z)>100||std::abs(yaw)>360||std::hypot(vx,vy)>15)throw std::runtime_error("Terrain probe outside map limits");
 auto s=context.data.sample(x,y,z,3);if(!s.found)throw std::runtime_error("Terrain probe has no support");
 auto pawn=context.pawn;d(pawn,0x7760)=x/100;d(pawn,0x7758)=y/100;d(pawn,0x7768)=yaw*terrain::pi/180;d(pawn,0x7780)=0;putVelocity(pawn,{vx,vy,0});b(pawn,0x94e6)=0;
 context.height=s.z;context.normal=s.normal;context.grounded=true;
 V p{x,y,s.z};auto a=terrain::attitude(s.normal,yaw*terrain::pi/180);V r{a[0],yaw,a[1]};
 reinterpret_cast<SetPose>(reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr))+PoseRva)(pawn,&p,&r,false,nullptr,0);
 write(L"terrain-probe-result.tsv",id+"\tOK\n");
}
}
extern "C" __declspec(dllexport) int RoweRCTerrainBind(void*){try{bind();}catch(const std::exception& e){write(L"terrain-error.txt",e.what());}return 0;}
extern "C" __declspec(dllexport) int RoweRCTerrainClear(void*){unbind();return 0;}
extern "C" __declspec(dllexport) int RoweRCTerrainProbe(void*){try{probe();}catch(const std::exception& e){write(L"terrain-probe-result.tsv",std::string("ERROR\t")+e.what());}return 0;}
