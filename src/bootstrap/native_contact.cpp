#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <bcrypt.h>
#include <algorithm>
#include <array>
#include <cmath>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>
#pragma comment(lib,"bcrypt.lib")

namespace {
constexpr size_t SimBytes=0x6f8, State=0x238;
struct Vec2 { double x,y; };
struct Footprint { std::vector<Vec2> boundary; std::vector<std::vector<Vec2>> parts; };
struct Report { bool hit; unsigned char pad[7]; double depth,impulse,correction; };
static_assert(sizeof(Footprint)==48 && sizeof(Report)==32);
using Simulation=std::array<unsigned char,SimBytes>;
using PairSolver=Report*(__fastcall*)(Report*,void*,void*,const Footprint*,const Footprint*);
std::filesystem::path bridgeRoot() {
    wchar_t p[32768]{}; if(!GetEnvironmentVariableW(L"LOCALAPPDATA",p,32768)) throw std::runtime_error("LOCALAPPDATA missing");
    return std::filesystem::path(p)/L"RoweRC"/L"bridge";
}
std::string read(const std::filesystem::path& p,size_t limit) {
    std::ifstream f(p,std::ios::binary); if(!f) throw std::runtime_error("native request/context missing");
    std::string s(limit+1,'\0'); f.read(s.data(),s.size()); s.resize(static_cast<size_t>(f.gcount()));
    if(s.size()>limit) throw std::runtime_error("native request exceeds limit"); return s;
}
void atomic(const std::filesystem::path& p,const std::string& text) {
    auto temp=p; temp+=L".native-tmp";
    {std::ofstream f(temp,std::ios::binary|std::ios::trunc); f<<text; if(!f) throw std::runtime_error("native response write failed");}
    if(!MoveFileExW(temp.c_str(),p.c_str(),MOVEFILE_REPLACE_EXISTING|MOVEFILE_WRITE_THROUGH)) throw std::runtime_error("native response commit failed");
}
std::vector<std::string> split(const std::string& s,char delimiter) {
    std::vector<std::string> v; size_t first=0;
    for(size_t i=0;i<=s.size();++i) if(i==s.size() || s[i]==delimiter) {v.push_back(s.substr(first,i-first));first=i+1;} return v;
}
double number(const std::string& s,double limit) {
    size_t used=0; double n=std::stod(s,&used);
    if(used!=s.size() || !std::isfinite(n) || std::abs(n)>limit) throw std::runtime_error("invalid native numeric input"); return n;
}
double get(const Simulation& s,size_t at) { double n; std::memcpy(&n,s.data()+at,8); return n; }
void put(Simulation& s,size_t at,double n) { std::memcpy(s.data()+at,&n,8); }
bool verifyGame() {
    wchar_t file[32768]{}; if(!GetModuleFileNameW(nullptr,file,32768)) return false;
    std::ifstream input(file,std::ios::binary); if(!input) return false;
    BCRYPT_ALG_HANDLE algorithm=nullptr; BCRYPT_HASH_HANDLE hash=nullptr;
    if(BCryptOpenAlgorithmProvider(&algorithm,BCRYPT_SHA256_ALGORITHM,nullptr,0)<0) return false;
    DWORD objectSize=0,received=0; bool ok=false;
    if(BCryptGetProperty(algorithm,BCRYPT_OBJECT_LENGTH,reinterpret_cast<PUCHAR>(&objectSize),sizeof(objectSize),&received,0)>=0) {
        std::vector<unsigned char> object(objectSize);
        if(BCryptCreateHash(algorithm,&hash,object.data(),objectSize,nullptr,0,0)>=0) {
            std::array<char,65536> block{}; bool valid=true;
            while(input) { input.read(block.data(),block.size()); auto size=static_cast<ULONG>(input.gcount());
                if(size && BCryptHashData(hash,reinterpret_cast<PUCHAR>(block.data()),size,0)<0) {valid=false;break;} }
            std::array<unsigned char,32> digest{};
            if(valid && BCryptFinishHash(hash,digest.data(),digest.size(),0)>=0) {
                std::ostringstream text; for(auto b:digest) text<<std::hex<<std::setfill('0')<<std::setw(2)<<static_cast<int>(b);
                ok=text.str()=="08da7cc9ca54d8f11dc05f70de7f33ed38014b33a82d2ccb4beb7f033592b4cf";
            }
            BCryptDestroyHash(hash);
        }
    }
    BCryptCloseAlgorithmProvider(algorithm,0); return ok;
}
Footprint footprint(const std::string& text) {
    if(text.size()>2300) throw std::runtime_error("native footprint exceeds limit");
    Footprint shape; size_t total=0;
    for(auto& encoded:split(text,';')) {
        std::vector<Vec2> polygon;
        for(auto& pair:split(encoded,'|')) {auto v=split(pair,':'); if(v.size()!=2) throw std::runtime_error("invalid native vertex");
            Vec2 point{number(v[0],1),number(v[1],1)}; polygon.push_back(point); shape.boundary.push_back(point);}
        if(polygon.size()<3 || polygon.size()>48 || (total+=polygon.size())>128) throw std::runtime_error("invalid native polygon count");
        double sign=0,area=0;
        for(size_t i=0;i<polygon.size();++i) {
            auto a=polygon[i],b=polygon[(i+1)%polygon.size()],c=polygon[(i+2)%polygon.size()];
            if(std::hypot(b.x-a.x,b.y-a.y)<.00001) throw std::runtime_error("degenerate native edge");
            double cross=(b.x-a.x)*(c.y-b.y)-(b.y-a.y)*(c.x-b.x);
            if(std::abs(cross)>1e-9) {if(sign*cross<0) throw std::runtime_error("nonconvex native polygon"); sign=cross;}
            area+=a.x*b.y-a.y*b.x;
        }
        if(std::abs(area)<.00001) throw std::runtime_error("degenerate native polygon");
        shape.parts.push_back(std::move(polygon));
    }
    if(shape.parts.empty() || shape.parts.size()>5) throw std::runtime_error("invalid native parts");
    auto points=shape.boundary;
    std::sort(points.begin(),points.end(),[](const Vec2& a,const Vec2& b){return a.x==b.x?a.y<b.y:a.x<b.x;});
    points.erase(std::unique(points.begin(),points.end(),[](const Vec2& a,const Vec2& b){return a.x==b.x&&a.y==b.y;}),points.end());
    auto cross=[](const Vec2& a,const Vec2& b,const Vec2& c){return (b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x);};
    std::vector<Vec2> hull;
    for(const auto& p:points){while(hull.size()>=2&&cross(hull[hull.size()-2],hull.back(),p)<=0)hull.pop_back();hull.push_back(p);}
    const auto lower=hull.size();
    for(auto it=points.rbegin()+1;it!=points.rend();++it){while(hull.size()>lower&&cross(hull[hull.size()-2],hull.back(),*it)<=0)hull.pop_back();hull.push_back(*it);}
    if(hull.size()>1)hull.pop_back();shape.boundary=std::move(hull);
    return shape;
}
struct Body {double x,y,yaw,vx,vy,w,mass,inertia; Footprint shape;};
Body body(const std::vector<std::string>& fields,size_t first) {
    Body b{number(fields[first],1e6),number(fields[first+1],1e6),number(fields[first+2],1e6),
        number(fields[first+3],30),number(fields[first+4],30),number(fields[first+5],30),
        number(fields[first+6],10),number(fields[first+7],2),footprint(fields[first+8])};
    if(b.mass<.1 || b.inertia<.0001) throw std::runtime_error("invalid native mass/inertia"); return b;
}
void stamp(Simulation& sim,const Body& b) {
    put(sim,0x50,b.mass); put(sim,0x68,b.inertia);
    put(sim,State+8,b.y); put(sim,State+16,b.x); put(sim,State+24,b.yaw);
    const double c=std::cos(b.yaw),s=std::sin(b.yaw);
    put(sim,State+32,c*b.vx+s*b.vy); put(sim,State+40,-s*b.vx+c*b.vy); put(sim,State+48,b.w);
}
DWORD protectedPair(PairSolver solver,Report* report,void* a,void* b,const Footprint* fa,const Footprint* fb) {
    __try {solver(report,a,b,fa,fb);return 0;} __except(EXCEPTION_EXECUTE_HANDLER) {return GetExceptionCode();}
}
void solve() {
    static const bool supported=verifyGame(); if(!supported) throw std::runtime_error("native solver: unsupported executable");
    const auto root=bridgeRoot(); auto context=split(read(root/L"native-context.tsv",1024),'\t');
    if(context.size()!=5 || context[0]!="RRCCTX1") throw std::runtime_error("invalid native context");
    size_t used=0; auto pawn=std::stoull(context[1],&used,16); if(used!=context[1].size() || pawn>UINTPTR_MAX-0x7518-SimBytes) throw std::runtime_error("invalid native pawn");
    Simulation original{}; SIZE_T copied=0;
    if(!ReadProcessMemory(GetCurrentProcess(),reinterpret_cast<void*>(pawn+0x7518),original.data(),original.size(),&copied) || copied!=original.size()) throw std::runtime_error("native simulation unavailable");
    const double x=number(context[2],1e8),y=number(context[3],1e8),yaw=number(context[4],360);
    if(std::abs(get(original,State+16)*100-x)>.2 || std::abs(get(original,State+8)*100-y)>.2 ||
        std::abs(std::remainder(get(original,State+24)*180/3.141592653589793-yaw,360))>.2) throw std::runtime_error("native simulation layout/pose mismatch");
    auto lines=split(read(root/L"native-request.tsv",131072),'\n'); auto header=split(lines.at(0),'\t');
    if(header.size()!=3 || header[0]!="RRCN1") throw std::runtime_error("invalid native batch header");
    const auto batch=std::stoll(header[1]); const auto count=std::stoi(header[2]);
    if(batch<=0 || count<1 || count>28 || lines.size()!=static_cast<size_t>(count+2) || !lines.back().empty()) throw std::runtime_error("invalid native batch size");
    auto solver=reinterpret_cast<PairSolver>(reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr))+0x0438ffa0);
    std::ostringstream output; output<<std::setprecision(17)<<"RRCN1\t"<<batch<<"\t"<<count<<"\n";
    for(int index=1;index<=count;++index) {
        auto fields=split(lines[index],'\t'); if(fields.size()!=19) throw std::runtime_error("invalid native pair record");
        const auto id=std::stoll(fields[0]); if(id<=0) throw std::runtime_error("invalid native pair ID");
        auto a=body(fields,1),b=body(fields,10); auto sa=original,sb=original; stamp(sa,a); stamp(sb,b);
        bool hit=false; double depth=0; DWORD failure=0;
        for(int pass=0;pass<3;++pass) {
            Report report{}; failure=protectedPair(solver,&report,sa.data(),sb.data(),&a.shape,&b.shape);
            if(failure || !report.hit) break; hit=true; depth=std::max(depth,report.depth);
        }
        if(failure) {output<<id<<"\tERROR\t"<<failure<<"\n";continue;}
        auto delta=[&](const Simulation& sim,const Body& before) {
            const double c=std::cos(before.yaw),s=std::sin(before.yaw),u=get(sim,State+32),v=get(sim,State+40);
            std::array<double,5> d{c*u-s*v-before.vx,s*u+c*v-before.vy,get(sim,State+48)-before.w,get(sim,State+16)-before.x,get(sim,State+8)-before.y};
            for(auto value:d) if(!std::isfinite(value)) throw std::runtime_error("non-finite native response"); return d;
        };
        auto da=delta(sa,a),db=delta(sb,b);
        double scale=std::min({1.0,4/std::max(.001,std::max(std::hypot(da[0],da[1]),std::hypot(db[0],db[1]))),10/std::max(.001,std::max(std::abs(da[2]),std::abs(db[2])))});
        output<<id<<"\t"<<(hit?1:0); for(auto value:da) output<<"\t"<<value*scale; for(auto value:db) output<<"\t"<<value*scale; output<<"\t"<<depth<<"\n";
    }
    atomic(root/L"native-response.tsv",output.str()); atomic(root/L"native-error.txt","");
}
}
extern "C" __declspec(dllexport) int RoweRCNativeSolve(void*) {
    try {solve();} catch(const std::exception& e) {try {atomic(bridgeRoot()/L"native-error.txt",e.what());}catch(...) {}}
    return 0;
}
