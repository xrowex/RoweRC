#include "../src/bootstrap/terrain_core.h"
#include <chrono>
#include <iostream>
#include <filesystem>
using namespace terrain;
void require(bool ok,const char* message){if(!ok)throw std::runtime_error(message);}
Triangle triangle(V a,V b,V c){Triangle t{};for(int i=0;i<3;++i){auto p=i==0?a:i==1?b:c;t.p[i*3]=float(p.x);t.p[i*3+1]=float(p.y);t.p[i*3+2]=float(p.z);}return t;}
void quad(Tree& t,V a,V b,V c,V d){t.triangles.push_back(triangle(a,b,c));t.triangles.push_back(triangle(a,c,d));}
int main(int argc,char** argv){try{
 Terrain t;quad(t.ground,{-100,-100,-10},{300,-100,-10},{300,100,-10},{-100,100,-10});
 quad(t.ground,{0,-50,0},{200,-50,20},{200,50,20},{0,50,0});
 quad(t.ground,{0,-50,50},{200,-50,50},{200,50,50},{0,50,50});t.ground.rebuild();
 auto s=t.sample(100,0,9,3);require(s.found&&std::abs(s.z-10)<1e-5,"ramp height");
 auto angles=attitude(s.normal,0);require(std::abs(angles[0]-std::atan(.1)*180/pi)<.001&&std::abs(angles[1])<.001,"ramp pitch");
 require(std::abs(t.sample(100,0,49).z-50)<1e-5,"upper deck");
 require(std::abs(t.sample(100,0,9).z-10)<1e-5,"do not snap to overhead deck");
 require(!t.sample(250,0,50).found,"do not snap down from a deck to distant ground");
 quad(t.walls,{50,-100,-10},{50,100,-10},{50,100,40},{50,-100,40});t.walls.rebuild();
 auto hit=t.move({0,0,0},{100,0,0},{10,0,0},0);
 require(hit.hit&&hit.position.x<=27.01&&hit.velocity.x<=0,"swept front capsule stops at wall");
 auto slide=t.move({0,0,0},{100,40,0},{10,4,0},0);
 require(slide.hit&&slide.position.x<=27.01&&slide.position.y>30&&slide.velocity.y>3.9,"wall sliding retains tangential motion");
 auto over=t.move({0,0,50},{100,0,50},{10,0,0},0);
 require(!over.hit&&std::abs(over.position.x-100)<.01,"lower deck wall does not hit upper deck");
 require(!t.sample(500,0,0).found,"missing support is explicit");
 if(argc>1){
   Terrain real;auto start=std::chrono::steady_clock::now();real.load(std::filesystem::path(argv[1]));
   auto spawn=real.sample(0,294.423169,0);require(spawn.found&&std::abs(spawn.z+.442593)<.02,"real city spawn");
   auto ramp=real.sample(-715.681611,-803.91078,4.5);require(ramp.found&&std::abs(ramp.z-4.760082)<.02,"real city raised road");
   auto far=real.sample(1900,266.7,0);require(far.found,"city supports driving beyond native 18 m boundary");
   double loadMs=std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-start).count();
   start=std::chrono::steady_clock::now();for(int i=0;i<10000;++i){auto q=real.sample(-715.681611+(i%17)*.01,-803.91078,4.5);require(q.found,"stable repeated sampling");}
   double queryUs=std::chrono::duration<double,std::micro>(std::chrono::steady_clock::now()-start).count()/10000;
   start=std::chrono::steady_clock::now();for(int i=0;i<1000;++i)real.move({0,294.423169,spawn.z},{1,294.423169,spawn.z},{1,0,0},0);
   double contactUs=std::chrono::duration<double,std::micro>(std::chrono::steady_clock::now()-start).count()/1000;
   V highest{0,0,-1e30};for(auto& t:real.ground.triangles){auto c=(t.v(0)+t.v(1)+t.v(2))*(1./3);if(c.z<=highest.z)continue;
     bool broad=true;for(V offset:{V{20,0,0},V{-20,0,0},V{0,15,0},V{0,-15,0}})if(!real.sample(c.x+offset.x,c.y+offset.y,c.z,3).found)broad=false;if(broad)highest=c;}
   double worstMs=0;int checked=0;
   for(size_t i=0;i<real.walls.triangles.size();i+=4000){auto& t=real.walls.triangles[i];auto p=(t.v(0)+t.v(1)+t.v(2))*(1./3);auto g=real.sample(p.x,p.y,p.z,3);if(!g.found||p.z-g.z>15)continue;p.z=g.z;auto at=std::chrono::steady_clock::now();real.move(p,p+V{1,1,0},{1,1,0},0);worstMs=std::max(worstMs,std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-at).count());++checked;}
   std::cout<<"{\"passed\":true,\"loadMs\":"<<loadMs<<",\"sampleUs\":"<<queryUs<<",\"contactUs\":"<<contactUs<<",\"worstContactMs\":"<<worstMs<<",\"wallLocationsChecked\":"<<checked<<",\"spawnZ\":"<<spawn.z<<",\"raisedRoadZ\":"<<ramp.z<<",\"farZ\":"<<far.z<<",\"highest\":["<<highest.x<<','<<highest.y<<','<<highest.z<<"]}\n";
 }else std::cout<<"PASS: slope, road layers, swept barrier collision, sliding and missing support\n";
 return 0;
}catch(const std::exception& e){std::cerr<<e.what()<<'\n';return 1;}}
