// R&S series mod icon generator. Usage: node harness/make-modicon.js About/ModIcon.png 64
// Copy the output to every Regions-and-societies repo's About/ModIcon.png.
const fs=require('fs'), zlib=require('zlib');
const OUT=process.argv[2], SIZE=+(process.argv[3]||64), SS=4, N=SIZE*SS;
// palette from Preview.png (neon regions on dark navy)
const REG=[[236,72,153],[34,211,238],[132,204,22],[139,92,246],[251,191,36],[59,130,246]];
const BORDER=[190,250,255], NAVY=[10,14,32], GLOW=[80,220,255];
// region seeds (unit disc coords)
const seeds=[[-0.45,-0.35],[0.35,-0.5],[0.55,0.25],[-0.15,0.55],[-0.6,0.25],[0.05,-0.05]];
// "R&S" wordmark: 5x7 bitmap glyphs, 1-col gaps
const G={R:["11110","10001","10001","11110","10100","10010","10001"],
         A:["01100","10010","10100","01000","10101","10010","01101"],
         S:["01111","10000","10000","01110","00001","00001","11110"]};
const WORD="RAS", COLS=17, ROWS=7, CELL=0.074; // cell size in unit-disc coords
const TX=-COLS*CELL/2, TY=-ROWS*CELL/2+0.02;
function glyphHit(u,v){
  const cx=Math.floor((u-TX)/CELL), cy=Math.floor((v-TY)/CELL);
  if(cx<0||cy<0||cx>=COLS||cy>=ROWS) return false;
  const gi=Math.floor(cx/6), gc=cx%6; if(gc===5) return false;
  return G[WORD[gi]][cy][gc]==='1';
}
const OW=0.04; // outline width
function textCover(u,v){
  if(glyphHit(u,v)) return 2;
  for(let a=0;a<12;a++){const t=a/12*Math.PI*2; if(glyphHit(u+Math.cos(t)*OW,v+Math.sin(t)*OW)) return 1;}
  return 0;
}
const px=new Float32Array(N*N*4);
function mix(a,b,t){return [a[0]+(b[0]-a[0])*t,a[1]+(b[1]-a[1])*t,a[2]+(b[2]-a[2])*t];}
for(let y=0;y<N;y++)for(let x=0;x<N;x++){
  const u=(x+0.5)/N*2-1, v=(y+0.5)/N*2-1, r=Math.hypot(u,v);
  let col=[0,0,0], a=0;
  const R=0.86;
  if(r<R){
    // nearest / second nearest seed
    let d1=9,d2=9,i1=0;
    seeds.forEach((s,i)=>{const d=Math.hypot(u-s[0],v-s[1]); if(d<d1){d2=d1;d1=d;i1=i;} else if(d<d2)d2=d;});
    col=REG[i1%REG.length].map(c=>c*0.85+6);
    const edge=d2-d1; // border line width
    if(edge<0.055) col=mix(BORDER,col,Math.min(1,edge/0.055));
    // rim darkening + inner glow ring
    const rim=(R-r)/R;
    if(rim<0.10) col=mix(GLOW,col,Math.min(1,rim/0.10*1.4));
    const tc=textCover(u,v);
    if(tc===2) col=[255,255,255]; else if(tc===1) col=mix(NAVY,col,0.15);
    a=1;
  } else if(r<0.98){
    // outer glow halo, fades to transparent
    const t=1-(r-R)/(0.98-R);
    col=GLOW; a=t*t*0.75;
  }
  // dark outline right at the disc edge for contrast on dark UI
  if(r>R-0.03&&r<R+0.02){ col=mix(NAVY,col,0.35); a=Math.max(a,1); }
  const o=(y*N+x)*4; px[o]=col[0];px[o+1]=col[1];px[o+2]=col[2];px[o+3]=a*255;
}
// downsample (premultiplied)
const out=Buffer.alloc(SIZE*SIZE*4);
for(let y=0;y<SIZE;y++)for(let x=0;x<SIZE;x++){
  let r=0,g=0,b=0,a=0;
  for(let j=0;j<SS;j++)for(let i=0;i<SS;i++){const o=((y*SS+j)*N+(x*SS+i))*4, pa=px[o+3]/255; r+=px[o]*pa;g+=px[o+1]*pa;b+=px[o+2]*pa;a+=pa;}
  const o=(y*SIZE+x)*4; if(a>0){out[o]=r/a;out[o+1]=g/a;out[o+2]=b/a;} out[o+3]=a/(SS*SS)*255;
}
// PNG encode
const raw=Buffer.alloc((SIZE*4+1)*SIZE);
for(let y=0;y<SIZE;y++){raw[y*(SIZE*4+1)]=0; out.copy(raw,y*(SIZE*4+1)+1,y*SIZE*4,(y+1)*SIZE*4);}
const crcT=[];for(let n=0;n<256;n++){let c=n;for(let k=0;k<8;k++)c=c&1?0xedb88320^(c>>>1):c>>>1;crcT[n]=c>>>0;}
const crc=b=>{let c=0xffffffff;for(const x of b)c=crcT[(c^x)&255]^(c>>>8);return (c^0xffffffff)>>>0;};
const chunk=(t,d)=>{const l=Buffer.alloc(4);l.writeUInt32BE(d.length);const td=Buffer.concat([Buffer.from(t),d]);const c=Buffer.alloc(4);c.writeUInt32BE(crc(td));return Buffer.concat([l,td,c]);};
const ihdr=Buffer.alloc(13);ihdr.writeUInt32BE(SIZE,0);ihdr.writeUInt32BE(SIZE,4);ihdr[8]=8;ihdr[9]=6;
fs.writeFileSync(OUT,Buffer.concat([Buffer.from([137,80,78,71,13,10,26,10]),chunk('IHDR',ihdr),chunk('IDAT',zlib.deflateSync(raw,{level:9})),chunk('IEND',Buffer.alloc(0))]));
console.log('wrote',OUT,SIZE+'x'+SIZE);
