#pragma once
#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <fstream>
#include <numeric>
#include <stdexcept>
#include <vector>

// Centimetres in world geometry; metres/seconds stay in the native solver.
namespace terrain {
constexpr double pi=3.14159265358979323846;
struct V { double x=0,y=0,z=0; V operator+(V b)const{return{x+b.x,y+b.y,z+b.z};}
 V operator-(V b)const{return{x-b.x,y-b.y,z-b.z};} V operator*(double s)const{return{x*s,y*s,z*s};} };
inline double dot(V a,V b){return a.x*b.x+a.y*b.y+a.z*b.z;}
inline V cross(V a,V b){return{a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x};}
inline double length(V a){return std::sqrt(dot(a,a));}
struct Triangle { float p[9]; V v(int i)const{return{p[i*3],p[i*3+1],p[i*3+2]};} };
struct Bounds { V lo{1e30,1e30,1e30},hi{-1e30,-1e30,-1e30};
 void add(V p){lo={std::min(lo.x,p.x),std::min(lo.y,p.y),std::min(lo.z,p.z)};hi={std::max(hi.x,p.x),std::max(hi.y,p.y),std::max(hi.z,p.z)};}
 bool overlaps(const Bounds& b)const{return lo.x<=b.hi.x&&hi.x>=b.lo.x&&lo.y<=b.hi.y&&hi.y>=b.lo.y&&lo.z<=b.hi.z&&hi.z>=b.lo.z;}
};
struct Tree {
 struct Node {Bounds box; uint32_t first=0,count=0,left=0,right=0;};
 std::vector<Triangle> triangles; std::vector<uint32_t> indices; std::vector<Node> nodes;
 uint32_t build(uint32_t first,uint32_t count){
   uint32_t index=(uint32_t)nodes.size(); nodes.emplace_back(); Bounds box,centers;
   for(uint32_t j=first;j<first+count;++j){auto& t=triangles[indices[j]];for(int k=0;k<3;++k)box.add(t.v(k));centers.add((t.v(0)+t.v(1)+t.v(2))*(1./3));}
   nodes[index].box=box;
   if(count<=12){nodes[index].first=first;nodes[index].count=count;return index;}
   V d=centers.hi-centers.lo;int axis=d.x>d.y?(d.x>d.z?0:2):(d.y>d.z?1:2);
   auto coordinate=[axis](const Triangle& t){V c=t.v(0)+t.v(1)+t.v(2);return axis==0?c.x:axis==1?c.y:c.z;};
   uint32_t mid=first+count/2;
   std::nth_element(indices.begin()+first,indices.begin()+mid,indices.begin()+first+count,[&](uint32_t a,uint32_t b){return coordinate(triangles[a])<coordinate(triangles[b]);});
   auto left=build(first,mid-first),right=build(mid,first+count-mid);nodes[index].left=left;nodes[index].right=right;return index;
 }
 void rebuild(){indices.resize(triangles.size());std::iota(indices.begin(),indices.end(),0);nodes.clear();nodes.reserve(triangles.size()/4+1);if(!indices.empty())build(0,(uint32_t)indices.size());}
 template<class F> void query(const Bounds& box,F&& f,uint32_t i=0)const{
   if(nodes.empty()||!nodes[i].box.overlaps(box))return;const auto& n=nodes[i];
   if(n.count){for(uint32_t j=n.first;j<n.first+n.count;++j)f(triangles[indices[j]]);}else{query(box,f,n.left);query(box,f,n.right);}
 }
};
struct Surface {bool found=false;double z=0;V normal{0,0,1};};
struct Terrain {
 Tree ground,walls;
 template<class Path> void load(const Path& path){
   std::ifstream f(path,std::ios::binary);char magic[8]{};uint32_t g=0,w=0;
   f.read(magic,8);f.read((char*)&g,4);f.read((char*)&w,4);
   if(!f||std::string(magic,8)!=std::string("RRCTRN1\0",8)||!g||g>2000000||w>6000000)throw std::runtime_error("Unsupported terrain data");
   auto read=[&](Tree& tree,uint32_t count){tree.triangles.resize(count);f.read((char*)tree.triangles.data(),size_t(count)*sizeof(Triangle));
     if(!f)throw std::runtime_error("Truncated terrain data");for(auto& t:tree.triangles)for(float v:t.p)if(!std::isfinite(v)||std::abs(v)>30000)throw std::runtime_error("Invalid terrain geometry");tree.rebuild();};
   read(ground,g);read(walls,w);if(f.peek()!=EOF)throw std::runtime_error("Trailing terrain data");
 }
 Surface sample(double x,double y,double reference,double reach=3)const{
   Surface result;double highest=-1e30;
   Bounds box;box.lo={x-.001,y-.001,reference-reach};box.hi={x+.001,y+.001,reference+reach};
   ground.query(box,[&](const Triangle& t){V a=t.v(0),b=t.v(1)-a,c=t.v(2)-a;double det=b.x*c.y-b.y*c.x;if(std::abs(det)<1e-9)return;
     double dx=x-a.x,dy=y-a.y,u=(dx*c.y-dy*c.x)/det,v=(b.x*dy-b.y*dx)/det;
     if(u<-.00001||v<-.00001||u+v>1.00001)return;double z=a.z+u*b.z+v*c.z;
     if(z>reference+reach+.001||z<reference-reach-.001||z<highest)return;V n=cross(b,c);if(n.z<0)n=n*-1;double l=length(n);if(l<1e-9)return;
     highest=z;result={true,z,n*(1/l)};
   });return result;
 }
 struct Contact {V position,velocity;bool hit=false;};
 Contact move(V from,V to,V velocity,double yaw,double halfLength=23,double radius=12)const{
   Contact out{from,velocity,false};V delta=to-from;int steps=std::clamp(int(std::ceil(std::hypot(delta.x,delta.y)/2)),1,100);
   V step=delta*(1./steps),forward{std::cos(yaw),std::sin(yaw),0};double end=std::max(0.,halfLength-radius);
   for(int s=0;s<steps;++s){out.position=out.position+step;
     for(int pass=0;pass<3;++pass){bool adjusted=false;
       for(double anchor:{-end,0.,end}){V center=out.position+forward*anchor;Bounds box;box.lo={center.x-radius,center.y-radius,center.z+1};box.hi={center.x+radius,center.y+radius,center.z+10};
         walls.query(box,[&](const Triangle& t){
           for(double h:{1.5,4.,8.}){double z=center.z+h;std::array<V,3> hits{};int count=0;
             for(int i=0;i<3;++i){V a=t.v(i),b=t.v((i+1)%3);if((a.z<=z&&b.z>z)||(b.z<=z&&a.z>z))hits[count++]=a+(b-a)*((z-a.z)/(b.z-a.z));}
             if(count!=2)continue;V a=hits[0],d=hits[1]-a;d.z=0;double len=dot(d,d);if(len<1e-10)continue;
             double u=std::clamp(((center.x-a.x)*d.x+(center.y-a.y)*d.y)/len,0.,1.);V p=a+d*u;
             V diff{center.x-p.x,center.y-p.y,0};double dist=length(diff);if(dist>=radius)continue;
             V normal;if(dist>1e-7)normal=diff*(1/dist);else {normal={-d.y,d.x,0};normal=normal*(1/length(normal));if(dot(normal,from-p)<0)normal=normal*-1;}
             V correction=normal*(radius-dist+.005);out.position=out.position+correction;center=center+correction;
             double vn=dot(out.velocity,normal);if(vn<0)out.velocity=out.velocity-normal*(1.03*vn);
             out.hit=adjusted=true;
           }
         });
       }if(!adjusted)break;
     }
   }out.position.z=to.z;return out;
 }
};
inline std::array<double,2> attitude(V n,double yaw){
 // UE forward rises for positive pitch; positive roll lowers the right side.
 double c=std::cos(yaw),s=std::sin(yaw);
 return{std::atan2(-(n.x*c+n.y*s),n.z)*180/pi,std::atan2(-n.x*s+n.y*c,n.z)*180/pi};
}
}
