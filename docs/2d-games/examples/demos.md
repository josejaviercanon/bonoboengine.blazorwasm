# Interactive Demos

Live, playable demonstrations of core mechanics from the toolkit. Click/drag to interact.

---

## Easing Curves

All 31 easing functions visualized. Select a curve to see it animate in real-time.

<div id="easing-demo" style="background:#1a1a2e;border-radius:8px;padding:1rem;margin:1rem 0">
<canvas id="easing-canvas" width="760" height="300" style="width:100%;border-radius:4px;cursor:pointer"></canvas>
<div id="easing-btns" style="display:flex;flex-wrap:wrap;gap:4px;margin-top:8px"></div>
</div>

<script>
(function(){
  const canvas = document.getElementById('easing-canvas');
  const ctx = canvas.getContext('2d');
  const btnContainer = document.getElementById('easing-btns');

  const ease = {
    Linear: t => t,
    QuadIn: t => t*t,
    QuadOut: t => t*(2-t),
    QuadInOut: t => t<.5?2*t*t:-1+(4-2*t)*t,
    CubicIn: t => t*t*t,
    CubicOut: t => (--t)*t*t+1,
    CubicInOut: t => t<.5?4*t*t*t:(t-1)*(2*t-2)*(2*t-2)+1,
    SineIn: t => 1-Math.cos(t*Math.PI/2),
    SineOut: t => Math.sin(t*Math.PI/2),
    SineInOut: t => -(Math.cos(Math.PI*t)-1)/2,
    ExpoIn: t => t===0?0:Math.pow(2,10*(t-1)),
    ExpoOut: t => t===1?1:1-Math.pow(2,-10*t),
    CircIn: t => 1-Math.sqrt(1-t*t),
    CircOut: t => Math.sqrt(1-(--t)*t),
    BackIn: t => 2.70158*t*t*t-1.70158*t*t,
    BackOut: t => {const c=1.70158;return 1+(c+1)*Math.pow(t-1,3)+c*Math.pow(t-1,2);},
    ElasticOut: t => t===0?0:t===1?1:Math.pow(2,-10*t)*Math.sin((t-0.075)*(2*Math.PI)/0.3)+1,
    BounceOut: t => {if(t<1/2.75)return 7.5625*t*t;if(t<2/2.75)return 7.5625*(t-=1.5/2.75)*t+.75;if(t<2.5/2.75)return 7.5625*(t-=2.25/2.75)*t+.9375;return 7.5625*(t-=2.625/2.75)*t+.984375;},
  };

  let current = 'QuadOut';
  let anim = 0;
  let animDir = 1;

  function createButtons(){
    Object.keys(ease).forEach(name => {
      const btn = document.createElement('button');
      btn.textContent = name;
      btn.style.cssText = 'padding:2px 8px;font-size:11px;border:1px solid #444;background:'+(name===current?'#ff6a35':'#2a2a3e')+';color:#eee;border-radius:3px;cursor:pointer;font-family:monospace';
      btn.onclick = () => { current = name; anim = 0; animDir = 1; createButtons(); };
      btnContainer.appendChild(btn);
    });
  }

  function draw(){
    ctx.fillStyle = '#1a1a2e';
    ctx.fillRect(0,0,canvas.width,canvas.height);

    const pad = 40, w = canvas.width-pad*2, h = canvas.height-pad*2;
    const fn = ease[current];

    ctx.strokeStyle = '#333';
    ctx.lineWidth = 1;
    for(let i=0;i<=10;i++){
      const x = pad+w*i/10, y = pad+h*i/10;
      ctx.beginPath();ctx.moveTo(x,pad);ctx.lineTo(x,pad+h);ctx.stroke();
      ctx.beginPath();ctx.moveTo(pad,y);ctx.lineTo(pad+w,y);ctx.stroke();
    }

    ctx.strokeStyle = '#ff6a35';
    ctx.lineWidth = 3;
    ctx.beginPath();
    for(let i=0;i<=200;i++){
      const t=i/200, x=pad+t*w, y=pad+h-fn(t)*h;
      i===0?ctx.moveTo(x,y):ctx.lineTo(x,y);
    }
    ctx.stroke();

    const ballT = fn(anim);
    const bx = pad+anim*w, by = pad+h-ballT*h;
    ctx.fillStyle = '#ff6a35';
    ctx.beginPath();ctx.arc(bx,by,8,0,Math.PI*2);ctx.fill();
    ctx.fillStyle = '#ff6a3544';
    ctx.beginPath();ctx.arc(bx,by,14,0,Math.PI*2);ctx.fill();

    ctx.fillStyle = '#888';
    ctx.font = '12px monospace';
    ctx.fillText('0', pad-15, pad+h+4);
    ctx.fillText('1', pad+w+5, pad+h+4);
    ctx.fillText('1', pad-15, pad+4);
    ctx.fillStyle = '#ff6a35';
    ctx.font = 'bold 14px monospace';
    ctx.fillText(current, pad, pad-10);

    anim += 0.008 * animDir;
    if(anim > 1){anim=1;animDir=-1;}
    if(anim < 0){anim=0;animDir=1;}

    requestAnimationFrame(draw);
  }

  btnContainer.innerHTML = '';
  createButtons();
  draw();
})();
</script>

---

## A* Pathfinding

Click to place walls, right-click to set start/end. Watch A* find the shortest path.

<div id="pathfinding-demo" style="background:#1a1a2e;border-radius:8px;padding:1rem;margin:1rem 0">
<canvas id="path-canvas" width="760" height="400" style="width:100%;border-radius:4px;cursor:crosshair"></canvas>
<p style="color:#888;font-size:12px;margin:8px 0 0">Left click: toggle wall · Right click: set start (green) then end (red) · Space: find path</p>
</div>

<script>
(function(){
  const canvas = document.getElementById('path-canvas');
  const ctx = canvas.getContext('2d');
  const COLS=38, ROWS=20, SIZE=20;
  const grid = Array.from({length:ROWS},()=>Array(COLS).fill(0));
  let start={x:1,y:1}, end={x:COLS-2,y:ROWS-2};
  let path=[], visited=[], settingStart=true;

  function heuristic(a,b){return Math.abs(a.x-b.x)+Math.abs(a.y-b.y);}

  function astar(){
    const open=[{...start,g:0,f:heuristic(start,end),parent:null}];
    const closed=new Set();
    visited=[];path=[];

    while(open.length){
      open.sort((a,b)=>a.f-b.f);
      const cur=open.shift();
      const key=cur.x+','+cur.y;
      if(closed.has(key))continue;
      closed.add(key);
      visited.push({x:cur.x,y:cur.y});

      if(cur.x===end.x&&cur.y===end.y){
        let n=cur;while(n){path.unshift({x:n.x,y:n.y});n=n.parent;}
        return;
      }

      for(const[dx,dy]of[[0,-1],[1,0],[0,1],[-1,0]]){
        const nx=cur.x+dx,ny=cur.y+dy;
        if(nx<0||ny<0||nx>=COLS||ny>=ROWS||grid[ny][nx]||closed.has(nx+','+ny))continue;
        const g=cur.g+1;
        open.push({x:nx,y:ny,g,f:g+heuristic({x:nx,y:ny},end),parent:cur});
      }
    }
  }

  function draw(){
    ctx.fillStyle='#1a1a2e';ctx.fillRect(0,0,canvas.width,canvas.height);
    for(let y=0;y<ROWS;y++)for(let x=0;x<COLS;x++){
      const px=x*SIZE,py=y*SIZE;
      if(grid[y][x]){ctx.fillStyle='#444';ctx.fillRect(px+1,py+1,SIZE-2,SIZE-2);}
      else{ctx.strokeStyle='#2a2a3e';ctx.strokeRect(px,py,SIZE,SIZE);}
    }
    visited.forEach(v=>{ctx.fillStyle='#ff6a3522';ctx.fillRect(v.x*SIZE+1,v.y*SIZE+1,SIZE-2,SIZE-2);});
    path.forEach(p=>{ctx.fillStyle='#ff6a35';ctx.fillRect(p.x*SIZE+2,p.y*SIZE+2,SIZE-4,SIZE-4);});
    ctx.fillStyle='#4ade80';ctx.fillRect(start.x*SIZE+2,start.y*SIZE+2,SIZE-4,SIZE-4);
    ctx.fillStyle='#f87171';ctx.fillRect(end.x*SIZE+2,end.y*SIZE+2,SIZE-4,SIZE-4);
  }

  canvas.addEventListener('click',e=>{
    const r=canvas.getBoundingClientRect();
    const x=Math.floor((e.clientX-r.left)/(r.width/COLS));
    const y=Math.floor((e.clientY-r.top)/(r.height/ROWS));
    if(x>=0&&y>=0&&x<COLS&&y<ROWS){
      grid[y][x]=grid[y][x]?0:1;
      path=[];visited=[];draw();
    }
  });

  canvas.addEventListener('contextmenu',e=>{
    e.preventDefault();
    const r=canvas.getBoundingClientRect();
    const x=Math.floor((e.clientX-r.left)/(r.width/COLS));
    const y=Math.floor((e.clientY-r.top)/(r.height/ROWS));
    if(x>=0&&y>=0&&x<COLS&&y<ROWS&&!grid[y][x]){
      if(settingStart){start={x,y};}else{end={x,y};}
      settingStart=!settingStart;
      path=[];visited=[];draw();
    }
  });

  document.addEventListener('keydown',e=>{
    if(e.code==='Space'){e.preventDefault();astar();draw();}
  });

  for(let i=0;i<80;i++){
    const x=Math.floor(Math.random()*COLS),y=Math.floor(Math.random()*ROWS);
    if((x!==start.x||y!==start.y)&&(x!==end.x||y!==end.y))grid[y][x]=1;
  }
  draw();
})();
</script>

---

## Particle System

A simple particle emitter. Move your mouse to control the emission point.

<div id="particle-demo" style="background:#1a1a2e;border-radius:8px;padding:1rem;margin:1rem 0">
<canvas id="particle-canvas" width="760" height="350" style="width:100%;border-radius:4px;cursor:none"></canvas>
</div>

<script>
(function(){
  const canvas = document.getElementById('particle-canvas');
  const ctx = canvas.getContext('2d');
  let mx=canvas.width/2, my=canvas.height/2;
  const particles=[];

  canvas.addEventListener('mousemove',e=>{
    const r=canvas.getBoundingClientRect();
    mx=(e.clientX-r.left)*(canvas.width/r.width);
    my=(e.clientY-r.top)*(canvas.height/r.height);
  });

  function spawn(){
    const angle=Math.random()*Math.PI*2;
    const speed=1+Math.random()*3;
    const life=30+Math.random()*60;
    const size=2+Math.random()*4;
    const hue=15+Math.random()*30;
    particles.push({x:mx,y:my,vx:Math.cos(angle)*speed,vy:Math.sin(angle)*speed-2,life,maxLife:life,size,hue});
  }

  function update(){
    ctx.fillStyle='rgba(26,26,46,0.15)';
    ctx.fillRect(0,0,canvas.width,canvas.height);

    for(let i=0;i<5;i++)spawn();

    for(let i=particles.length-1;i>=0;i--){
      const p=particles[i];
      p.x+=p.vx;p.y+=p.vy;p.vy+=0.05;p.life--;
      const alpha=p.life/p.maxLife;
      const s=p.size*alpha;
      ctx.fillStyle=`hsla(${p.hue},100%,${50+30*alpha}%,${alpha})`;
      ctx.fillRect(p.x-s/2,p.y-s/2,s,s);
      if(p.life<=0)particles.splice(i,1);
    }

    requestAnimationFrame(update);
  }
  update();
})();
</script>

---

## Spring Physics

Click and drag the ball. Watch it spring back with damped oscillation — the same math used for screen shake, camera follow, and juicy UI.

<div id="spring-demo" style="background:#1a1a2e;border-radius:8px;padding:1rem;margin:1rem 0">
<canvas id="spring-canvas" width="760" height="300" style="width:100%;border-radius:4px;cursor:grab"></canvas>
<div style="display:flex;gap:1rem;margin-top:8px;flex-wrap:wrap">
<label style="color:#888;font-size:12px">Stiffness: <input id="spring-k" type="range" min="0.01" max="0.5" step="0.01" value="0.08" style="width:100px"></label>
<label style="color:#888;font-size:12px">Damping: <input id="spring-d" type="range" min="0.8" max="0.99" step="0.01" value="0.92" style="width:100px"></label>
</div>
</div>

<script>
(function(){
  const canvas = document.getElementById('spring-canvas');
  const ctx = canvas.getContext('2d');
  const cx=canvas.width/2, cy=canvas.height/2;
  let bx=cx, by=cy, vx=0, vy=0;
  let dragging=false, trail=[];

  canvas.addEventListener('mousedown',e=>{dragging=true;canvas.style.cursor='grabbing';});
  canvas.addEventListener('mouseup',()=>{dragging=false;canvas.style.cursor='grab';});
  canvas.addEventListener('mousemove',e=>{
    if(!dragging)return;
    const r=canvas.getBoundingClientRect();
    bx=(e.clientX-r.left)*(canvas.width/r.width);
    by=(e.clientY-r.top)*(canvas.height/r.height);
    vx=0;vy=0;
  });

  function update(){
    const k=parseFloat(document.getElementById('spring-k').value);
    const d=parseFloat(document.getElementById('spring-d').value);

    if(!dragging){
      const dx=cx-bx, dy=cy-by;
      vx+=dx*k;vy+=dy*k;
      vx*=d;vy*=d;
      bx+=vx;by+=vy;
    }

    trail.push({x:bx,y:by});
    if(trail.length>40)trail.shift();

    ctx.fillStyle='#1a1a2e';ctx.fillRect(0,0,canvas.width,canvas.height);

    ctx.strokeStyle='#333';ctx.lineWidth=2;
    ctx.beginPath();ctx.moveTo(cx,cy);ctx.lineTo(bx,by);ctx.stroke();
    ctx.fillStyle='#555';ctx.beginPath();ctx.arc(cx,cy,6,0,Math.PI*2);ctx.fill();

    trail.forEach((p,i)=>{
      const a=i/trail.length;
      ctx.fillStyle=`rgba(255,106,53,${a*0.3})`;
      ctx.beginPath();ctx.arc(p.x,p.y,8*a,0,Math.PI*2);ctx.fill();
    });

    ctx.fillStyle='#ff6a35';ctx.beginPath();ctx.arc(bx,by,14,0,Math.PI*2);ctx.fill();
    ctx.fillStyle='#ffa07a';ctx.beginPath();ctx.arc(bx-4,by-4,5,0,Math.PI*2);ctx.fill();

    requestAnimationFrame(update);
  }
  update();
})();
</script>

---

## Cellular Automata Cave Generation

Click "Generate" to create a new cave. Adjust iterations to see how the automata evolve.

<div id="cave-demo" style="background:#1a1a2e;border-radius:8px;padding:1rem;margin:1rem 0">
<canvas id="cave-canvas" width="760" height="380" style="width:100%;border-radius:4px"></canvas>
<div style="display:flex;gap:1rem;margin-top:8px;align-items:center;flex-wrap:wrap">
<button id="cave-gen" style="padding:4px 16px;background:#ff6a35;color:#fff;border:none;border-radius:4px;cursor:pointer;font-family:monospace">Generate</button>
<label style="color:#888;font-size:12px">Fill %: <input id="cave-fill" type="range" min="40" max="60" value="48" style="width:80px"><span id="cave-fill-val">48</span></label>
<label style="color:#888;font-size:12px">Iterations: <input id="cave-iter" type="range" min="1" max="10" value="5" style="width:80px"><span id="cave-iter-val">5</span></label>
</div>
</div>

<script>
(function(){
  const canvas = document.getElementById('cave-canvas');
  const ctx = canvas.getContext('2d');
  const W=95,H=47,S=8;

  function generate(){
    const fill=parseInt(document.getElementById('cave-fill').value);
    const iters=parseInt(document.getElementById('cave-iter').value);
    document.getElementById('cave-fill-val').textContent=fill;
    document.getElementById('cave-iter-val').textContent=iters;

    let grid=Array.from({length:H},(_,y)=>Array.from({length:W},(_,x)=>{
      if(x===0||y===0||x===W-1||y===H-1)return 1;
      return Math.random()*100<fill?1:0;
    }));

    for(let i=0;i<iters;i++){
      const next=grid.map(r=>[...r]);
      for(let y=1;y<H-1;y++)for(let x=1;x<W-1;x++){
        let walls=0;
        for(let dy=-1;dy<=1;dy++)for(let dx=-1;dx<=1;dx++){
          if(dx===0&&dy===0)continue;
          walls+=grid[y+dy][x+dx];
        }
        next[y][x]=walls>=5?1:walls<=2?0:grid[y][x];
      }
      grid=next;
    }

    ctx.fillStyle='#1a1a2e';ctx.fillRect(0,0,canvas.width,canvas.height);
    for(let y=0;y<H;y++)for(let x=0;x<W;x++){
      if(grid[y][x]){
        ctx.fillStyle='#3a3a5e';ctx.fillRect(x*S,y*S,S,S);
      }else{
        ctx.fillStyle='#8b6914';ctx.fillRect(x*S,y*S,S,S);
        ctx.fillStyle='#a07818';ctx.fillRect(x*S+1,y*S+1,S-2,S-2);
      }
    }
  }

  document.getElementById('cave-gen').onclick=generate;
  document.getElementById('cave-fill').oninput=generate;
  document.getElementById('cave-iter').oninput=generate;
  generate();
})();
</script>

---

## Tweening Playground

See how different tween properties animate a game object. Combines easing curves with real transforms.

<div id="tween-demo" style="background:#1a1a2e;border-radius:8px;padding:1rem;margin:1rem 0">
<canvas id="tween-canvas" width="760" height="200" style="width:100%;border-radius:4px"></canvas>
<div style="display:flex;gap:8px;margin-top:8px;flex-wrap:wrap">
<button class="tw-btn" data-prop="position" style="padding:2px 10px;font-size:12px;border:1px solid #ff6a35;background:#ff6a35;color:#fff;border-radius:3px;cursor:pointer;font-family:monospace">Position</button>
<button class="tw-btn" data-prop="scale" style="padding:2px 10px;font-size:12px;border:1px solid #444;background:#2a2a3e;color:#eee;border-radius:3px;cursor:pointer;font-family:monospace">Scale</button>
<button class="tw-btn" data-prop="rotation" style="padding:2px 10px;font-size:12px;border:1px solid #444;background:#2a2a3e;color:#eee;border-radius:3px;cursor:pointer;font-family:monospace">Rotation</button>
<button class="tw-btn" data-prop="color" style="padding:2px 10px;font-size:12px;border:1px solid #444;background:#2a2a3e;color:#eee;border-radius:3px;cursor:pointer;font-family:monospace">Color</button>
<button class="tw-btn" data-prop="all" style="padding:2px 10px;font-size:12px;border:1px solid #444;background:#2a2a3e;color:#eee;border-radius:3px;cursor:pointer;font-family:monospace">All</button>
</div>
</div>

<script>
(function(){
  const canvas = document.getElementById('tween-canvas');
  const ctx = canvas.getContext('2d');
  let prop='position', t=0;
  const backOut=t=>{const c=1.70158;return 1+(c+1)*Math.pow(t-1,3)+c*Math.pow(t-1,2);};

  document.querySelectorAll('.tw-btn').forEach(btn=>{
    btn.onclick=()=>{
      prop=btn.dataset.prop;t=0;
      document.querySelectorAll('.tw-btn').forEach(b=>{b.style.background='#2a2a3e';b.style.borderColor='#444';});
      btn.style.background='#ff6a35';btn.style.borderColor='#ff6a35';
    };
  });

  function draw(){
    ctx.fillStyle='#1a1a2e';ctx.fillRect(0,0,canvas.width,canvas.height);
    t+=0.005;if(t>1)t=0;
    const et=backOut(Math.min(t*2,1));

    let x=100,y=canvas.height/2,s=1,rot=0,r=255,g=106,b=53;

    if(prop==='position'||prop==='all')x=100+et*560;
    if(prop==='scale'||prop==='all')s=0.3+et*1.2;
    if(prop==='rotation'||prop==='all')rot=et*Math.PI*4;
    if(prop==='color'||prop==='all'){r=Math.floor(255-et*180);g=Math.floor(106+et*100);b=Math.floor(53+et*200);}

    for(let i=0;i<5;i++){
      const tt=Math.max(0,t-i*0.03);
      const et2=backOut(Math.min(tt*2,1));
      let gx=100,gy=canvas.height/2;
      if(prop==='position'||prop==='all')gx=100+et2*560;
      ctx.fillStyle=`rgba(255,106,53,${0.05*(5-i)})`;
      ctx.fillRect(gx-15,gy-15,30,30);
    }

    ctx.save();
    ctx.translate(x,y);
    ctx.rotate(rot);
    ctx.scale(s,s);
    ctx.fillStyle=`rgb(${r},${g},${b})`;
    ctx.fillRect(-15,-15,30,30);
    ctx.strokeStyle='#fff3';
    ctx.strokeRect(-15,-15,30,30);
    ctx.restore();

    ctx.fillStyle='#333';ctx.fillRect(50,canvas.height-20,canvas.width-100,4);
    ctx.fillStyle='#ff6a35';ctx.fillRect(50,canvas.height-20,(canvas.width-100)*t,4);

    requestAnimationFrame(draw);
  }
  draw();
})();
</script>

---

# Collision Detection

---

## AABB vs AABB Collision

Drag the two boxes around. They highlight when their axis-aligned bounding boxes overlap.

<div id="aabb-demo" style="background:#1a1a2e;border-radius:8px;padding:1rem;margin:1rem 0">
<canvas id="aabb-canvas" width="760" height="350" style="width:100%;border-radius:4px;cursor:grab"></canvas>
<p style="color:#888;font-size:12px;margin:8px 0 0">Drag either box to test overlap</p>
</div>

<script>
(function(){
  const canvas = document.getElementById('aabb-canvas');
  const ctx = canvas.getContext('2d');
  const boxes = [
    {x:200,y:120,w:120,h:80},
    {x:400,y:160,w:100,h:100}
  ];
  let drag=null, ox=0, oy=0;

  function aabbOverlap(a,b){
    return a.x<b.x+b.w && a.x+a.w>b.x && a.y<b.y+b.h && a.y+a.h>b.y;
  }

  function toCanvas(e){
    const r=canvas.getBoundingClientRect();
    return {x:(e.clientX-r.left)*(canvas.width/r.width),y:(e.clientY-r.top)*(canvas.height/r.height)};
  }

  canvas.addEventListener('mousedown',e=>{
    const p=toCanvas(e);
    for(let i=boxes.length-1;i>=0;i--){
      const b=boxes[i];
      if(p.x>=b.x&&p.x<=b.x+b.w&&p.y>=b.y&&p.y<=b.y+b.h){
        drag=i;ox=p.x-b.x;oy=p.y-b.y;return;
      }
    }
  });
  canvas.addEventListener('mousemove',e=>{
    if(drag===null)return;
    const p=toCanvas(e);
    boxes[drag].x=p.x-ox;boxes[drag].y=p.y-oy;
  });
  canvas.addEventListener('mouseup',()=>{drag=null;});
  canvas.addEventListener('mouseleave',()=>{drag=null;});

  function draw(){
    ctx.fillStyle='#1a1a2e';ctx.fillRect(0,0,canvas.width,canvas.height);
    const hit=aabbOverlap(boxes[0],boxes[1]);

    boxes.forEach((b,i)=>{
      ctx.strokeStyle=hit?'#ff4444':'#ff6a35';
      ctx.fillStyle=hit?'rgba(255,68,68,0.15)':'rgba(255,106,53,0.1)';
      ctx.lineWidth=2;
      ctx.fillRect(b.x,b.y,b.w,b.h);
      ctx.strokeRect(b.x,b.y,b.w,b.h);
      ctx.fillStyle='#888';ctx.font='11px monospace';
      ctx.fillText(`Box ${i+1}: (${Math.round(b.x)}, ${Math.round(b.y)})`,b.x,b.y-6);
    });

    ctx.fillStyle=hit?'#ff4444':'#4ade80';
    ctx.font='bold 14px monospace';
    ctx.fillText(hit?'COLLIDING':'No collision',10,25);

    requestAnimationFrame(draw);
  }
  draw();
})();
</script>

---

## Circle vs Circle Collision

Drag the circles. They highlight when overlapping, with the overlap distance shown.

<div id="circle-col-demo" style="background:#1a1a2e;border-radius:8px;padding:1rem;margin:1rem 0">
<canvas id="circle-col-canvas" width="760" height="350" style="width:100%;border-radius:4px;cursor:grab"></canvas>
<p style="color:#888;font-size:12px;margin:8px 0 0">Drag circles to test collision</p>
</div>

<script>
(function(){
  const canvas = document.getElementById('circle-col-canvas');
  const ctx = canvas.getContext('2d');
  const circles=[{x:250,y:175,r:60},{x:480,y:175,r:45}];
  let drag=null,ox=0,oy=0;

  function toCanvas(e){
    const r=canvas.getBoundingClientRect();
    return{x:(e.clientX-r.left)*(canvas.width/r.width),y:(e.clientY-r.top)*(canvas.height/r.height)};
  }

  canvas.addEventListener('mousedown',e=>{
    const p=toCanvas(e);
    for(let i=circles.length-1;i>=0;i--){
      const c=circles[i];
      const dx=p.x-c.x,dy=p.y-c.y;
      if(dx*dx+dy*dy<=c.r*c.r){drag=i;ox=dx;oy=dy;return;}
    }
  });
  canvas.addEventListener('mousemove',e=>{
    if(drag===null)return;
    const p=toCanvas(e);
    circles[drag].x=p.x-ox;circles[drag].y=p.y-oy;
  });
  canvas.addEventListener('mouseup',()=>{drag=null;});
  canvas.addEventListener('mouseleave',()=>{drag=null;});

  function draw(){
    ctx.fillStyle='#1a1a2e';ctx.fillRect(0,0,canvas.width,canvas.height);
    const a=circles[0],b=circles[1];
    const dx=b.x-a.x,dy=b.y-a.y;
    const dist=Math.sqrt(dx*dx+dy*dy);
    const hit=dist<a.r+b.r;

    // Connection line
    ctx.strokeStyle='#333';ctx.lineWidth=1;
    ctx.beginPath();ctx.moveTo(a.x,a.y);ctx.lineTo(b.x,b.y);ctx.stroke();

    circles.forEach(c=>{
      ctx.beginPath();ctx.arc(c.x,c.y,c.r,0,Math.PI*2);
      ctx.fillStyle=hit?'rgba(255,68,68,0.15)':'rgba(255,106,53,0.1)';
      ctx.fill();
      ctx.strokeStyle=hit?'#ff4444':'#ff6a35';
      ctx.lineWidth=2;ctx.stroke();
      // Center dot
      ctx.fillStyle='#ff6a35';ctx.beginPath();ctx.arc(c.x,c.y,3,0,Math.PI*2);ctx.fill();
    });

    ctx.fillStyle='#888';ctx.font='12px monospace';
    ctx.fillText(`Distance: ${dist.toFixed(1)}  Sum radii: ${(a.r+b.r).toFixed(1)}`,10,canvas.height-15);
    ctx.fillStyle=hit?'#ff4444':'#4ade80';ctx.font='bold 14px monospace';
    ctx.fillText(hit?'COLLIDING':'No collision',10,25);

    requestAnimationFrame(draw);
  }
  draw();
})();
</script>

---

## SAT Polygon Collision

Drag convex polygons to test Separating Axis Theorem collision. The MTV (Minimum Translation Vector) is shown on overlap.

<div id="sat-demo" style="background:#1a1a2e;border-radius:8px;padding:1rem;margin:1rem 0">
<canvas id="sat-canvas" width="760" height="400" style="width:100%;border-radius:4px;cursor:grab"></canvas>
<p style="color:#888;font-size:12px;margin:8px 0 0">Drag polygons · MTV arrow shows minimum separation direction</p>
</div>

<script>
(function(){
  const canvas = document.getElementById('sat-canvas');
  const ctx = canvas.getContext('2d');

  function makePoly(cx,cy,sides,r){
    const pts=[];
    for(let i=0;i<sides;i++){
      const a=Math.PI*2*i/sides-Math.PI/2;
      pts.push({x:cx+Math.cos(a)*r,y:cy+Math.sin(a)*r});
    }
    return{cx,cy,pts,sides,r};
  }

  const polys=[makePoly(250,200,5,70),makePoly(450,200,4,60)];
  let drag=null,ox=0,oy=0;

  function movePoly(p,dx,dy){
    p.cx+=dx;p.cy+=dy;
    p.pts.forEach(pt=>{pt.x+=dx;pt.y+=dy;});
  }

  function toCanvas(e){
    const r=canvas.getBoundingClientRect();
    return{x:(e.clientX-r.left)*(canvas.width/r.width),y:(e.clientY-r.top)*(canvas.height/r.height)};
  }

  function pointInPoly(px,py,pts){
    let inside=false;
    for(let i=0,j=pts.length-1;i<pts.length;j=i++){
      const xi=pts[i].x,yi=pts[i].y,xj=pts[j].x,yj=pts[j].y;
      if(((yi>py)!==(yj>py))&&(px<(xj-xi)*(py-yi)/(yj-yi)+xi))inside=!inside;
    }
    return inside;
  }

  function getAxes(pts){
    const axes=[];
    for(let i=0;i<pts.length;i++){
      const j=(i+1)%pts.length;
      const edge={x:pts[j].x-pts[i].x,y:pts[j].y-pts[i].y};
      const len=Math.sqrt(edge.x*edge.x+edge.y*edge.y);
      axes.push({x:-edge.y/len,y:edge.x/len});
    }
    return axes;
  }

  function project(pts,axis){
    let min=Infinity,max=-Infinity;
    pts.forEach(p=>{
      const d=p.x*axis.x+p.y*axis.y;
      if(d<min)min=d;if(d>max)max=d;
    });
    return{min,max};
  }

  function satTest(a,b){
    const axes=[...getAxes(a),...getAxes(b)];
    let minOverlap=Infinity,mtvAxis=null;
    for(const axis of axes){
      const pa=project(a,axis),pb=project(b,axis);
      const overlap=Math.min(pa.max-pb.min,pb.max-pa.min);
      if(overlap<=0)return null;
      if(overlap<minOverlap){minOverlap=overlap;mtvAxis=axis;}
    }
    // Ensure MTV points from a to b
    const d={x:polys[1].cx-polys[0].cx,y:polys[1].cy-polys[0].cy};
    if(d.x*mtvAxis.x+d.y*mtvAxis.y<0){mtvAxis={x:-mtvAxis.x,y:-mtvAxis.y};}
    return{overlap:minOverlap,axis:mtvAxis};
  }

  canvas.addEventListener('mousedown',e=>{
    const p=toCanvas(e);
    for(let i=polys.length-1;i>=0;i--){
      if(pointInPoly(p.x,p.y,polys[i].pts)){
        drag=i;ox=p.x-polys[i].cx;oy=p.y-polys[i].cy;return;
      }
    }
  });
  canvas.addEventListener('mousemove',e=>{
    if(drag===null)return;
    const p=toCanvas(e);
    const dx=p.x-ox-polys[drag].cx,dy=p.y-oy-polys[drag].cy;
    movePoly(polys[drag],dx,dy);
  });
  canvas.addEventListener('mouseup',()=>{drag=null;});
  canvas.addEventListener('mouseleave',()=>{drag=null;});

  function draw(){
    ctx.fillStyle='#1a1a2e';ctx.fillRect(0,0,canvas.width,canvas.height);
    const result=satTest(polys[0].pts,polys[1].pts);
    const hit=result!==null;

    polys.forEach(p=>{
      ctx.beginPath();
      p.pts.forEach((pt,i)=>i===0?ctx.moveTo(pt.x,pt.y):ctx.lineTo(pt.x,pt.y));
      ctx.closePath();
      ctx.fillStyle=hit?'rgba(255,68,68,0.15)':'rgba(255,106,53,0.1)';
      ctx.fill();
      ctx.strokeStyle=hit?'#ff4444':'#ff6a35';
      ctx.lineWidth=2;ctx.stroke();
    });

    if(result){
      const midX=(polys[0].cx+polys[1].cx)/2,midY=(polys[0].cy+polys[1].cy)/2;
      const ax=result.axis.x*result.overlap,ay=result.axis.y*result.overlap;
      ctx.strokeStyle='#4ade80';ctx.lineWidth=3;
      ctx.beginPath();ctx.moveTo(midX,midY);ctx.lineTo(midX+ax*2,midY+ay*2);ctx.stroke();
      // Arrow head
      const ang=Math.atan2(ay,ax);
      ctx.beginPath();
      ctx.moveTo(midX+ax*2,midY+ay*2);
      ctx.lineTo(midX+ax*2-Math.cos(ang-0.4)*12,midY+ay*2-Math.sin(ang-0.4)*12);
      ctx.moveTo(midX+ax*2,midY+ay*2);
      ctx.lineTo(midX+ax*2-Math.cos(ang+0.4)*12,midY+ay*2-Math.sin(ang+0.4)*12);
      ctx.stroke();

      ctx.fillStyle='#4ade80';ctx.font='12px monospace';
      ctx.fillText(`MTV: ${result.overlap.toFixed(1)}px`,10,canvas.height-15);
    }

    ctx.fillStyle=hit?'#ff4444':'#4ade80';ctx.font='bold 14px monospace';
    ctx.fillText(hit?'COLLIDING (SAT)':'No collision',10,25);

    requestAnimationFrame(draw);
  }
  draw();
})();
</script>

---

# Camera Systems

---

## Camera Follow with Deadzone

Move the character with WASD or arrow keys. The camera only follows when the character leaves the deadzone rectangle.

<div id="deadzone-demo" style="background:#1a1a2e;border-radius:8px;padding:1rem;margin:1rem 0">
<canvas id="deadzone-canvas" width="760" height="400" style="width:100%;border-radius:4px;outline:none" tabindex="0"></canvas>
<div style="display:flex;gap:1rem;margin-top:8px;flex-wrap:wrap">
<label style="color:#888;font-size:12px">Deadzone W: <input id="dz-w" type="range" min="20" max="300" value="120" style="width:80px"><span id="dz-w-val">120</span></label>
<label style="color:#888;font-size:12px">Deadzone H: <input id="dz-h" type="range" min="20" max="200" value="80" style="width:80px"><span id="dz-h-val">80</span></label>
</div>
<p style="color:#888;font-size:12px;margin:8px 0 0">Click canvas first, then use WASD or arrow keys to move</p>
</div>

<script>
(function(){
  const canvas = document.getElementById('deadzone-canvas');
  const ctx = canvas.getContext('2d');
  const W=760,H=400;
  const worldW=2000,worldH=1200;
  let px=worldW/2,py=worldH/2,camX=px-W/2,camY=py-H/2;
  const keys={};
  const speed=3;

  // Generate world objects
  const trees=[];
  for(let i=0;i<60;i++)trees.push({x:Math.random()*worldW,y:Math.random()*worldH,s:10+Math.random()*20});

  canvas.addEventListener('keydown',e=>{keys[e.key]=true;e.preventDefault();});
  canvas.addEventListener('keyup',e=>{keys[e.key]=false;});
  canvas.addEventListener('click',()=>canvas.focus());

  document.getElementById('dz-w').oninput=function(){document.getElementById('dz-w-val').textContent=this.value;};
  document.getElementById('dz-h').oninput=function(){document.getElementById('dz-h-val').textContent=this.value;};

  function update(){
    if(keys['ArrowLeft']||keys['a'])px-=speed;
    if(keys['ArrowRight']||keys['d'])px+=speed;
    if(keys['ArrowUp']||keys['w'])py-=speed;
    if(keys['ArrowDown']||keys['s'])py+=speed;
    px=Math.max(10,Math.min(worldW-10,px));
    py=Math.max(10,Math.min(worldH-10,py));

    const dzW=parseInt(document.getElementById('dz-w').value);
    const dzH=parseInt(document.getElementById('dz-h').value);
    const dzL=camX+W/2-dzW/2, dzR=camX+W/2+dzW/2;
    const dzT=camY+H/2-dzH/2, dzB=camY+H/2+dzH/2;

    if(px<dzL)camX-=dzL-px;
    if(px>dzR)camX+=px-dzR;
    if(py<dzT)camY-=dzT-py;
    if(py>dzB)camY+=py-dzB;
    camX=Math.max(0,Math.min(worldW-W,camX));
    camY=Math.max(0,Math.min(worldH-H,camY));

    ctx.fillStyle='#1a1a2e';ctx.fillRect(0,0,W,H);

    // Grid
    ctx.strokeStyle='#2a2a3e';ctx.lineWidth=1;
    const gs=60;
    const sx=-(camX%gs),sy=-(camY%gs);
    for(let x=sx;x<W;x+=gs){ctx.beginPath();ctx.moveTo(x,0);ctx.lineTo(x,H);ctx.stroke();}
    for(let y=sy;y<H;y+=gs){ctx.beginPath();ctx.moveTo(0,y);ctx.lineTo(W,y);ctx.stroke();}

    // Trees
    trees.forEach(t=>{
      const sx=t.x-camX,sy=t.y-camY;
      if(sx>-30&&sx<W+30&&sy>-30&&sy<H+30){
        ctx.fillStyle='#2d5a2d';
        ctx.beginPath();ctx.arc(sx,sy-t.s,t.s,0,Math.PI*2);ctx.fill();
        ctx.fillStyle='#5a3a1a';ctx.fillRect(sx-3,sy-5,6,10);
      }
    });

    // Player
    const spx=px-camX,spy=py-camY;
    ctx.fillStyle='#ff6a35';
    ctx.fillRect(spx-8,spy-8,16,16);
    ctx.fillStyle='#ffa07a';
    ctx.fillRect(spx-6,spy-6,4,4);

    // Deadzone rect
    ctx.strokeStyle='#ff6a3566';ctx.lineWidth=1;ctx.setLineDash([4,4]);
    ctx.strokeRect(W/2-dzW/2,H/2-dzH/2,dzW,dzH);
    ctx.setLineDash([]);

    ctx.fillStyle='#888';ctx.font='11px monospace';
    ctx.fillText(`Player: (${Math.round(px)}, ${Math.round(py)})  Camera: (${Math.round(camX)}, ${Math.round(camY)})`,10,H-10);

    requestAnimationFrame(update);
  }
  update();
  canvas.focus();
})();
</script>

---

## Camera Smoothing / Lerp

Toggle between instant, lerp, and SmoothDamp camera follow modes. Move the target with your mouse.

<div id="camsmooth-demo" style="background:#1a1a2e;border-radius:8px;padding:1rem;margin:1rem 0">
<canvas id="camsmooth-canvas" width="760" height="350" style="width:100%;border-radius:4px;cursor:crosshair"></canvas>
<div style="display:flex;gap:8px;margin-top:8px;flex-wrap:wrap">
<button class="cs-btn" data-mode="instant" style="padding:2px 10px;font-size:12px;border:1px solid #444;background:#2a2a3e;color:#eee;border-radius:3px;cursor:pointer;font-family:monospace">Instant</button>
<button class="cs-btn" data-mode="lerp" style="padding:2px 10px;font-size:12px;border:1px solid #ff6a35;background:#ff6a35;color:#fff;border-radius:3px;cursor:pointer;font-family:monospace">Lerp</button>
<button class="cs-btn" data-mode="smoothdamp" style="padding:2px 10px;font-size:12px;border:1px solid #444;background:#2a2a3e;color:#eee;border-radius:3px;cursor:pointer;font-family:monospace">SmoothDamp</button>
</div>
<p style="color:#888;font-size:12px;margin:8px 0 0">Move mouse to set target position · Camera follows using selected mode</p>
</div>

<script>
(function(){
  const canvas = document.getElementById('camsmooth-canvas');
  const ctx = canvas.getContext('2d');
  let mode='lerp';
  let tx=canvas.width/2,ty=canvas.height/2;
  let cx=tx,cy=ty;
  let vx=0,vy=0; // for SmoothDamp

  canvas.addEventListener('mousemove',e=>{
    const r=canvas.getBoundingClientRect();
    tx=(e.clientX-r.left)*(canvas.width/r.width);
    ty=(e.clientY-r.top)*(canvas.height/r.height);
  });

  document.querySelectorAll('.cs-btn').forEach(btn=>{
    btn.onclick=()=>{
      mode=btn.dataset.mode;
      document.querySelectorAll('.cs-btn').forEach(b=>{b.style.background='#2a2a3e';b.style.borderColor='#444';});
      btn.style.background='#ff6a35';btn.style.borderColor='#ff6a35';
    };
  });

  function smoothDamp(current,target,vel,smoothTime){
    const omega=2/smoothTime;
    const x=omega*(1/60);
    const exp=1/(1+x+0.48*x*x+0.235*x*x*x);
    const change=current-target;
    const temp=(vel+omega*change)*(1/60);
    vel=(vel-omega*temp)*exp;
    return{val:target+(change+temp)*exp,vel};
  }

  function draw(){
    ctx.fillStyle='#1a1a2e';ctx.fillRect(0,0,canvas.width,canvas.height);

    if(mode==='instant'){cx=tx;cy=ty;}
    else if(mode==='lerp'){cx+=(tx-cx)*0.05;cy+=(ty-cy)*0.05;}
    else{
      const rx=smoothDamp(cx,tx,vx,0.3);cx=rx.val;vx=rx.vel;
      const ry=smoothDamp(cy,ty,vy,0.3);cy=ry.val;vy=ry.vel;
    }

    // Line from camera to target
    ctx.strokeStyle='#333';ctx.lineWidth=1;
    ctx.beginPath();ctx.moveTo(cx,cy);ctx.lineTo(tx,ty);ctx.stroke();

    // Target crosshair
    ctx.strokeStyle='#4ade8066';ctx.lineWidth=1;
    ctx.beginPath();ctx.moveTo(tx-15,ty);ctx.lineTo(tx+15,ty);ctx.stroke();
    ctx.beginPath();ctx.moveTo(tx,ty-15);ctx.lineTo(tx,ty+15);ctx.stroke();

    // Camera
    ctx.fillStyle='#ff6a35';ctx.beginPath();ctx.arc(cx,cy,12,0,Math.PI*2);ctx.fill();
    ctx.fillStyle='#1a1a2e';ctx.font='bold 10px monospace';ctx.textAlign='center';
    ctx.fillText('C',cx,cy+3.5);ctx.textAlign='left';

    ctx.fillStyle='#888';ctx.font='12px monospace';
    ctx.fillText(`Mode: ${mode}  |  Distance: ${Math.sqrt((tx-cx)**2+(ty-cy)**2).toFixed(1)}px`,10,25);

    requestAnimationFrame(draw);
  }
  draw();
})();
</script>

---

## Screen Shake

Click the canvas to trigger screen shake. Adjust intensity and decay.

<div id="shake-demo" style="background:#1a1a2e;border-radius:8px;padding:1rem;margin:1rem 0">
<canvas id="shake-canvas" width="760" height="350" style="width:100%;border-radius:4px;cursor:pointer"></canvas>
<div style="display:flex;gap:1rem;margin-top:8px;flex-wrap:wrap">
<label style="color:#888;font-size:12px">Intensity: <input id="shake-int" type="range" min="2" max="30" value="12" style="width:100px"><span id="shake-int-val">12</span></label>
<label style="color:#888;font-size:12px">Decay: <input id="shake-dec" type="range" min="0.8" max="0.98" step="0.01" value="0.92" style="width:100px"><span id="shake-dec-val">0.92</span></label>
</div>
<p style="color:#888;font-size:12px;margin:8px 0 0">Click anywhere to trigger shake</p>
</div>

<script>
(function(){
  const canvas = document.getElementById('shake-canvas');
  const ctx = canvas.getContext('2d');
  let shakeX=0,shakeY=0,trauma=0;

  document.getElementById('shake-int').oninput=function(){document.getElementById('shake-int-val').textContent=this.value;};
  document.getElementById('shake-dec').oninput=function(){document.getElementById('shake-dec-val').textContent=this.value;};

  canvas.addEventListener('click',()=>{trauma=1;});

  // Scene objects
  const objs=[];
  for(let i=0;i<15;i++)objs.push({x:50+Math.random()*660,y:50+Math.random()*250,w:20+Math.random()*40,h:20+Math.random()*40,c:`hsl(${Math.random()*360},40%,40%)`});

  function draw(){
    const intensity=parseFloat(document.getElementById('shake-int').value);
    const decay=parseFloat(document.getElementById('shake-dec').value);

    if(trauma>0.01){
      shakeX=(Math.random()*2-1)*trauma*intensity;
      shakeY=(Math.random()*2-1)*trauma*intensity;
      trauma*=decay;
    }else{shakeX=0;shakeY=0;trauma=0;}

    ctx.fillStyle='#1a1a2e';ctx.fillRect(0,0,canvas.width,canvas.height);
    ctx.save();
    ctx.translate(shakeX,shakeY);

    // Ground
    ctx.fillStyle='#2a2a3e';ctx.fillRect(0,canvas.height-60,canvas.width,60);

    objs.forEach(o=>{
      ctx.fillStyle=o.c;ctx.fillRect(o.x,o.y,o.w,o.h);
    });

    // Character
    ctx.fillStyle='#ff6a35';ctx.fillRect(canvas.width/2-15,canvas.height-90,30,30);
    ctx.fillStyle='#ffa07a';ctx.fillRect(canvas.width/2-10,canvas.height-85,8,8);

    ctx.restore();

    // Trauma meter
    ctx.fillStyle='#333';ctx.fillRect(10,10,150,12);
    ctx.fillStyle='#ff6a35';ctx.fillRect(10,10,150*trauma,12);
    ctx.fillStyle='#888';ctx.font='11px monospace';
    ctx.fillText(`Trauma: ${trauma.toFixed(2)}`,170,20);

    requestAnimationFrame(draw);
  }
  draw();
})();
</script>

---

# Parallax Scrolling

---

## Multi-Layer Parallax

4 layers scrolling at different speeds. Move your mouse left/right to control, or let it auto-scroll.

<div id="parallax-demo" style="background:#1a1a2e;border-radius:8px;padding:1rem;margin:1rem 0">
<canvas id="parallax-canvas" width="760" height="350" style="width:100%;border-radius:4px"></canvas>
<p style="color:#888;font-size:12px;margin:8px 0 0">Move mouse left/right to control parallax · Auto-scrolls when idle</p>
</div>

<script>
(function(){
  const canvas = document.getElementById('parallax-canvas');
  const ctx = canvas.getContext('2d');
  const W=760,H=350;
  let mx=W/2, mouseActive=false, lastMove=0;

  canvas.addEventListener('mousemove',e=>{
    const r=canvas.getBoundingClientRect();
    mx=(e.clientX-r.left)*(W/r.width);
    mouseActive=true;lastMove=Date.now();
  });
  canvas.addEventListener('mouseleave',()=>{mouseActive=false;});

  const layers=[
    {speed:0.1,color:'#0a0a1e',shapes:[]},
    {speed:0.3,color:'#151530',shapes:[]},
    {speed:0.6,color:'#1e1e40',shapes:[]},
    {speed:1.0,color:'#2a2a50',shapes:[]}
  ];

  // Generate shapes for each layer
  layers.forEach((l,li)=>{
    const count=8+li*4;
    for(let i=0;i<count;i++){
      const type=li<2?'mountain':'tree';
      l.shapes.push({
        x:Math.random()*W*2,
        w:type==='mountain'?80+Math.random()*120:10+Math.random()*15,
        h:type==='mountain'?60+Math.random()*80+li*20:30+Math.random()*40+li*10,
        type
      });
    }
  });

  let scroll=0;
  function draw(){
    if(Date.now()-lastMove>2000)mouseActive=false;
    const target=mouseActive?(mx/W-0.5)*4:1;
    scroll+=target;

    ctx.fillStyle='#0a0a1e';ctx.fillRect(0,0,W,H);

    // Stars
    ctx.fillStyle='#ffffff22';
    for(let i=0;i<30;i++){
      const sx=(i*73+scroll*0.02)%W;
      const sy=(i*37)%((H/2));
      ctx.fillRect(sx,sy,1.5,1.5);
    }

    layers.forEach((l,li)=>{
      const offset=scroll*l.speed;
      const baseY=H-40-li*30;

      // Ground band
      ctx.fillStyle=l.color;
      ctx.fillRect(0,baseY,W,H-baseY);

      l.shapes.forEach(s=>{
        let sx=((s.x-offset)%(W*2)+W*2)%(W*2)-W*0.5;
        if(sx>W+200||sx<-200)return;
        if(s.type==='mountain'){
          ctx.fillStyle=l.color;
          ctx.beginPath();
          ctx.moveTo(sx-s.w/2,baseY);
          ctx.lineTo(sx,baseY-s.h);
          ctx.lineTo(sx+s.w/2,baseY);
          ctx.fill();
        }else{
          ctx.fillStyle=`hsl(${130+li*10},${30+li*10}%,${15+li*5}%)`;
          ctx.beginPath();
          ctx.arc(sx,baseY-s.h,s.w,0,Math.PI*2);
          ctx.fill();
          ctx.fillStyle='#3a2a1a';
          ctx.fillRect(sx-2,baseY-s.h+s.w/2,4,s.h-s.w/2);
        }
      });
    });

    // Layer labels
    ctx.fillStyle='#666';ctx.font='10px monospace';
    layers.forEach((l,i)=>{
      ctx.fillText(`Layer ${i+1}: ${l.speed}x`,W-100,15+i*14);
    });

    requestAnimationFrame(draw);
  }
  draw();
})();
</script>

---

# Game Loop

---

## Variable vs Fixed Timestep

Two balls moving right: one uses variable dt, one uses a fixed timestep. Watch them diverge, especially when the tab loses focus or frame rate drops.

<div id="timestep-demo" style="background:#1a1a2e;border-radius:8px;padding:1rem;margin:1rem 0">
<canvas id="timestep-canvas" width="760" height="250" style="width:100%;border-radius:4px"></canvas>
<div style="display:flex;gap:8px;margin-top:8px">
<button id="ts-reset" style="padding:4px 16px;background:#ff6a35;color:#fff;border:none;border-radius:4px;cursor:pointer;font-family:monospace">Reset</button>
<button id="ts-lag" style="padding:4px 16px;background:#2a2a3e;color:#eee;border:1px solid #444;border-radius:4px;cursor:pointer;font-family:monospace">Simulate Lag (200ms)</button>
</div>
<p style="color:#888;font-size:12px;margin:8px 0 0">Watch how variable timestep diverges under lag</p>
</div>

<script>
(function(){
  const canvas = document.getElementById('timestep-canvas');
  const ctx = canvas.getContext('2d');
  const SPEED=120; // px per second
  const FIXED_DT=1/60;
  let varX=60, fixX=60, accum=0;
  let lastTime=performance.now();
  let simLag=false;

  document.getElementById('ts-reset').onclick=()=>{varX=60;fixX=60;accum=0;lastTime=performance.now();};
  document.getElementById('ts-lag').onclick=function(){
    simLag=true;
    this.style.background='#ff6a35';
    setTimeout(()=>{simLag=false;this.style.background='#2a2a3e';},200);
  };

  function update(now){
    let dt=(now-lastTime)/1000;
    lastTime=now;
    if(simLag)dt+=0.2;
    dt=Math.min(dt,0.25);

    // Variable timestep
    varX+=SPEED*dt;
    if(varX>700)varX=60;

    // Fixed timestep
    accum+=dt;
    while(accum>=FIXED_DT){
      fixX+=SPEED*FIXED_DT;
      accum-=FIXED_DT;
    }
    if(fixX>700)fixX=60;

    ctx.fillStyle='#1a1a2e';ctx.fillRect(0,0,canvas.width,canvas.height);

    // Labels
    ctx.fillStyle='#888';ctx.font='12px monospace';
    ctx.fillText('Variable dt',10,80);
    ctx.fillText('Fixed dt (1/60)',10,170);

    // Tracks
    ctx.fillStyle='#2a2a3e';
    ctx.fillRect(60,70,640,30);
    ctx.fillRect(60,160,640,30);

    // Variable ball
    ctx.fillStyle='#ff6a35';
    ctx.beginPath();ctx.arc(varX,85,12,0,Math.PI*2);ctx.fill();

    // Fixed ball
    ctx.fillStyle='#4ade80';
    ctx.beginPath();ctx.arc(fixX,175,12,0,Math.PI*2);ctx.fill();

    // Divergence
    const diff=Math.abs(varX-fixX);
    ctx.fillStyle='#888';ctx.font='11px monospace';
    ctx.fillText(`Divergence: ${diff.toFixed(1)}px`,10,canvas.height-15);
    ctx.fillText(`Frame dt: ${(dt*1000).toFixed(1)}ms`,300,canvas.height-15);

    requestAnimationFrame(update);
  }
  requestAnimationFrame(update);
})();
</script>

---

# Coordinate Systems

---

## World / Screen Space Converter

Click on the canvas to see world vs screen coordinates. Pan with right-drag, zoom with scroll wheel.

<div id="coords-demo" style="background:#1a1a2e;border-radius:8px;padding:1rem;margin:1rem 0">
<canvas id="coords-canvas" width="760" height="400" style="width:100%;border-radius:4px;cursor:crosshair"></canvas>
<p style="color:#888;font-size:12px;margin:8px 0 0">Left click: place marker · Right drag: pan · Scroll: zoom</p>
</div>

<script>
(function(){
  const canvas = document.getElementById('coords-canvas');
  const ctx = canvas.getContext('2d');
  let camX=0,camY=0,zoom=1;
  let panning=false,panStartX=0,panStartY=0,panCamX=0,panCamY=0;
  const markers=[];

  function toCanvas(e){
    const r=canvas.getBoundingClientRect();
    return{x:(e.clientX-r.left)*(canvas.width/r.width),y:(e.clientY-r.top)*(canvas.height/r.height)};
  }

  function screenToWorld(sx,sy){
    return{x:(sx-canvas.width/2)/zoom+camX,y:(sy-canvas.height/2)/zoom+camY};
  }

  function worldToScreen(wx,wy){
    return{x:(wx-camX)*zoom+canvas.width/2,y:(wy-camY)*zoom+canvas.height/2};
  }

  canvas.addEventListener('click',e=>{
    const s=toCanvas(e);
    const w=screenToWorld(s.x,s.y);
    markers.push({wx:w.x,wy:w.y,sx:s.x,sy:s.y});
    if(markers.length>10)markers.shift();
  });

  canvas.addEventListener('contextmenu',e=>e.preventDefault());
  canvas.addEventListener('mousedown',e=>{
    if(e.button===2){panning=true;const p=toCanvas(e);panStartX=p.x;panStartY=p.y;panCamX=camX;panCamY=camY;}
  });
  canvas.addEventListener('mousemove',e=>{
    if(!panning)return;
    const p=toCanvas(e);
    camX=panCamX-(p.x-panStartX)/zoom;
    camY=panCamY-(p.y-panStartY)/zoom;
  });
  canvas.addEventListener('mouseup',()=>{panning=false;});
  canvas.addEventListener('wheel',e=>{
    e.preventDefault();
    zoom*=e.deltaY>0?0.9:1.1;
    zoom=Math.max(0.2,Math.min(5,zoom));
  },{passive:false});

  function draw(){
    ctx.fillStyle='#1a1a2e';ctx.fillRect(0,0,canvas.width,canvas.height);

    // World grid
    const gridSize=50;
    ctx.strokeStyle='#2a2a3e';ctx.lineWidth=1;
    const tl=screenToWorld(0,0),br=screenToWorld(canvas.width,canvas.height);
    const startX=Math.floor(tl.x/gridSize)*gridSize;
    const startY=Math.floor(tl.y/gridSize)*gridSize;

    for(let wx=startX;wx<=br.x;wx+=gridSize){
      const s=worldToScreen(wx,0);
      ctx.strokeStyle=wx===0?'#ff6a3544':'#2a2a3e';
      ctx.lineWidth=wx===0?2:1;
      ctx.beginPath();ctx.moveTo(s.x,0);ctx.lineTo(s.x,canvas.height);ctx.stroke();
    }
    for(let wy=startY;wy<=br.y;wy+=gridSize){
      const s=worldToScreen(0,wy);
      ctx.strokeStyle=wy===0?'#ff6a3544':'#2a2a3e';
      ctx.lineWidth=wy===0?2:1;
      ctx.beginPath();ctx.moveTo(0,s.y);ctx.lineTo(canvas.width,s.y);ctx.stroke();
    }

    // Origin
    const origin=worldToScreen(0,0);
    ctx.fillStyle='#ff6a35';ctx.beginPath();ctx.arc(origin.x,origin.y,4,0,Math.PI*2);ctx.fill();
    ctx.fillStyle='#ff6a35';ctx.font='10px monospace';ctx.fillText('(0,0)',origin.x+6,origin.y-6);

    // Markers
    markers.forEach((m,i)=>{
      const s=worldToScreen(m.wx,m.wy);
      ctx.fillStyle='#4ade80';ctx.beginPath();ctx.arc(s.x,s.y,5,0,Math.PI*2);ctx.fill();
      ctx.fillStyle='#4ade80';ctx.font='10px monospace';
      ctx.fillText(`W(${Math.round(m.wx)},${Math.round(m.wy)})`,s.x+8,s.y-4);
      ctx.fillStyle='#888';
      ctx.fillText(`S(${Math.round(s.x)},${Math.round(s.y)})`,s.x+8,s.y+10);
    });

    // HUD
    ctx.fillStyle='#888';ctx.font='11px monospace';
    ctx.fillText(`Zoom: ${zoom.toFixed(2)}x  Camera: (${Math.round(camX)}, ${Math.round(camY)})`,10,20);

    requestAnimationFrame(draw);
  }
  draw();
})();
</script>

---

# Isometric

---

## Isometric Grid with Mouse Picking

Hover to highlight tiles, click to place/remove blocks. Shows cartesian↔iso coordinate conversion.

<div id="iso-demo" style="background:#1a1a2e;border-radius:8px;padding:1rem;margin:1rem 0">
<canvas id="iso-canvas" width="760" height="450" style="width:100%;border-radius:4px;cursor:pointer;image-rendering:pixelated"></canvas>
<p style="color:#888;font-size:12px;margin:8px 0 0">Hover to highlight · Click to place/remove blocks · Coordinates shown</p>
</div>

<script>
(function(){
  const canvas = document.getElementById('iso-canvas');
  const ctx = canvas.getContext('2d');
  const COLS=10,ROWS=10,TW=60,TH=30;
  const grid=Array.from({length:ROWS},()=>Array(COLS).fill(0));
  let hoverX=-1,hoverY=-1;
  const offsetX=canvas.width/2,offsetY=80;

  function cartToIso(cx,cy){
    return{x:(cx-cy)*TW/2+offsetX,y:(cx+cy)*TH/2+offsetY};
  }

  function isoToCart(sx,sy){
    const ax=sx-offsetX,ay=sy-offsetY;
    const cx=(ax/(TW/2)+ay/(TH/2))/2;
    const cy=(ay/(TH/2)-ax/(TW/2))/2;
    return{x:Math.floor(cx),y:Math.floor(cy)};
  }

  function toCanvas(e){
    const r=canvas.getBoundingClientRect();
    return{x:(e.clientX-r.left)*(canvas.width/r.width),y:(e.clientY-r.top)*(canvas.height/r.height)};
  }

  canvas.addEventListener('mousemove',e=>{
    const p=toCanvas(e);
    const c=isoToCart(p.x,p.y);
    if(c.x>=0&&c.x<COLS&&c.y>=0&&c.y<ROWS){hoverX=c.x;hoverY=c.y;}
    else{hoverX=-1;hoverY=-1;}
  });

  canvas.addEventListener('click',e=>{
    const p=toCanvas(e);
    const c=isoToCart(p.x,p.y);
    if(c.x>=0&&c.x<COLS&&c.y>=0&&c.y<ROWS){
      grid[c.y][c.x]=grid[c.y][c.x]?0:1;
    }
  });

  function drawTile(cx,cy,filled,hover){
    const p=cartToIso(cx,cy);
    const hw=TW/2,hh=TH/2;

    // Top face
    ctx.beginPath();
    ctx.moveTo(p.x,p.y-hh);ctx.lineTo(p.x+hw,p.y);
    ctx.lineTo(p.x,p.y+hh);ctx.lineTo(p.x-hw,p.y);ctx.closePath();

    if(filled){
      ctx.fillStyle=hover?'#ff8c5a':'#ff6a35';ctx.fill();
      ctx.strokeStyle='#cc5428';ctx.lineWidth=1;ctx.stroke();
      // Left face
      ctx.beginPath();ctx.moveTo(p.x-hw,p.y);ctx.lineTo(p.x,p.y+hh);
      ctx.lineTo(p.x,p.y+hh+15);ctx.lineTo(p.x-hw,p.y+15);ctx.closePath();
      ctx.fillStyle='#cc4420';ctx.fill();ctx.strokeStyle='#aa3318';ctx.stroke();
      // Right face
      ctx.beginPath();ctx.moveTo(p.x+hw,p.y);ctx.lineTo(p.x,p.y+hh);
      ctx.lineTo(p.x,p.y+hh+15);ctx.lineTo(p.x+hw,p.y+15);ctx.closePath();
      ctx.fillStyle='#dd5530';ctx.fill();ctx.strokeStyle='#bb4425';ctx.stroke();
    }else{
      ctx.fillStyle=hover?'rgba(255,106,53,0.2)':'rgba(255,255,255,0.03)';
      ctx.fill();
      ctx.strokeStyle=hover?'#ff6a35':'#3a3a5e';
      ctx.lineWidth=1;ctx.stroke();
    }
  }

  function draw(){
    ctx.fillStyle='#1a1a2e';ctx.fillRect(0,0,canvas.width,canvas.height);

    for(let y=0;y<ROWS;y++)for(let x=0;x<COLS;x++){
      drawTile(x,y,grid[y][x],x===hoverX&&y===hoverY);
    }

    // Coordinates
    if(hoverX>=0){
      const p=cartToIso(hoverX,hoverY);
      ctx.fillStyle='#ff6a35';ctx.font='12px monospace';
      ctx.fillText(`Cart: (${hoverX}, ${hoverY})  →  Iso: (${Math.round(p.x)}, ${Math.round(p.y)})`,10,canvas.height-15);
    }

    requestAnimationFrame(draw);
  }
  draw();
})();
</script>

---

# Character Controller

---

## Platformer Controller

Arrow keys to move and jump. Shows coyote time and jump buffer indicators — essential game-feel mechanics.

<div id="platformer-demo" style="background:#1a1a2e;border-radius:8px;padding:1rem;margin:1rem 0">
<canvas id="platformer-canvas" width="760" height="400" style="width:100%;border-radius:4px;outline:none" tabindex="0"></canvas>
<p style="color:#888;font-size:12px;margin:8px 0 0">Click canvas first · Arrow keys: move/jump · Watch coyote time &amp; jump buffer indicators</p>
</div>

<script>
(function(){
  const canvas = document.getElementById('platformer-canvas');
  const ctx = canvas.getContext('2d');
  const W=760,H=400;
  const GRAVITY=0.5, JUMP_VEL=-10, MOVE_SPEED=4;
  const COYOTE_TIME=8, JUMP_BUFFER=8;

  let px=100,py=300,vx=0,vy=0;
  let grounded=false, coyoteTimer=0, jumpBufferTimer=0;
  let facingRight=true;
  const keys={};

  const platforms=[
    {x:0,y:H-40,w:W,h:40},
    {x:200,y:300,w:150,h:15},
    {x:450,y:250,w:120,h:15},
    {x:150,y:200,w:100,h:15},
    {x:500,y:160,w:130,h:15},
    {x:300,y:120,w:100,h:15},
  ];

  canvas.addEventListener('keydown',e=>{keys[e.key]=true;e.preventDefault();
    if(e.key==='ArrowUp'||e.key===' ')jumpBufferTimer=JUMP_BUFFER;
  });
  canvas.addEventListener('keyup',e=>{keys[e.key]=false;});
  canvas.addEventListener('click',()=>canvas.focus());

  function update(){
    // Horizontal
    if(keys['ArrowLeft']){vx=-MOVE_SPEED;facingRight=false;}
    else if(keys['ArrowRight']){vx=MOVE_SPEED;facingRight=true;}
    else vx*=0.7;

    // Gravity
    vy+=GRAVITY;

    // Coyote time countdown
    if(grounded)coyoteTimer=COYOTE_TIME;
    else if(coyoteTimer>0)coyoteTimer--;

    // Jump buffer countdown
    if(jumpBufferTimer>0)jumpBufferTimer--;

    // Jump if both coyote and buffer active
    if(jumpBufferTimer>0&&coyoteTimer>0){
      vy=JUMP_VEL;coyoteTimer=0;jumpBufferTimer=0;
    }

    px+=vx;py+=vy;
    grounded=false;

    // Collision with platforms
    platforms.forEach(p=>{
      if(px+10>p.x&&px-10<p.x+p.w&&py+16>p.y&&py+16<p.y+p.h+vy+1&&vy>=0){
        py=p.y-16;vy=0;grounded=true;
      }
    });

    // Wrap
    if(px<-20)px=W+20;if(px>W+20)px=-20;
    if(py>H+50){py=100;vy=0;}

    // Draw
    ctx.fillStyle='#1a1a2e';ctx.fillRect(0,0,W,H);

    // Platforms
    platforms.forEach(p=>{
      ctx.fillStyle='#3a3a5e';ctx.fillRect(p.x,p.y,p.w,p.h);
      ctx.fillStyle='#4a4a6e';ctx.fillRect(p.x,p.y,p.w,3);
    });

    // Player
    const squash=grounded?1:vy<0?0.85:1.15;
    ctx.save();
    ctx.translate(px,py);
    ctx.scale(facingRight?1:-1,1);
    ctx.scale(1/squash,squash);
    ctx.fillStyle='#ff6a35';ctx.fillRect(-10,-16,20,16);
    ctx.fillStyle='#ffa07a';ctx.fillRect(-7,-13,5,5); // eye
    ctx.restore();

    // HUD - Coyote time indicator
    ctx.fillStyle='#333';ctx.fillRect(10,10,80,10);
    ctx.fillStyle=coyoteTimer>0?'#4ade80':'#555';
    ctx.fillRect(10,10,80*(coyoteTimer/COYOTE_TIME),10);
    ctx.fillStyle='#888';ctx.font='10px monospace';
    ctx.fillText('Coyote',95,19);

    // Jump buffer indicator
    ctx.fillStyle='#333';ctx.fillRect(10,25,80,10);
    ctx.fillStyle=jumpBufferTimer>0?'#60a5fa':'#555';
    ctx.fillRect(10,25,80*(jumpBufferTimer/JUMP_BUFFER),10);
    ctx.fillText('JumpBuf',95,34);

    ctx.fillStyle=grounded?'#4ade80':'#f87171';ctx.font='11px monospace';
    ctx.fillText(grounded?'GROUNDED':'AIRBORNE',10,52);

    requestAnimationFrame(update);
  }
  update();
  canvas.focus();
})();
</script>

---

# Tilemap Autotiling

---

## 4-Bit Bitmask Autotiling

Click to place/remove tiles. Autotile rules apply in real-time using a 4-bit bitmask (up/right/down/left). Each tile shows its bitmask value.

<div id="autotile-demo" style="background:#1a1a2e;border-radius:8px;padding:1rem;margin:1rem 0">
<canvas id="autotile-canvas" width="760" height="400" style="width:100%;border-radius:4px;cursor:pointer;image-rendering:pixelated"></canvas>
<div style="display:flex;gap:8px;margin-top:8px">
<button id="at-clear" style="padding:4px 16px;background:#2a2a3e;color:#eee;border:1px solid #444;border-radius:4px;cursor:pointer;font-family:monospace">Clear</button>
<button id="at-fill" style="padding:4px 16px;background:#ff6a35;color:#fff;border:none;border-radius:4px;cursor:pointer;font-family:monospace">Fill Random</button>
</div>
<p style="color:#888;font-size:12px;margin:8px 0 0">Click to place/remove tiles · Numbers show 4-bit bitmask (URDL)</p>
</div>

<script>
(function(){
  const canvas = document.getElementById('autotile-canvas');
  const ctx = canvas.getContext('2d');
  const COLS=19,ROWS=10,S=40;
  const grid=Array.from({length:ROWS},()=>Array(COLS).fill(0));
  let painting=false,paintVal=1;

  function toCanvas(e){
    const r=canvas.getBoundingClientRect();
    return{x:(e.clientX-r.left)*(canvas.width/r.width),y:(e.clientY-r.top)*(canvas.height/r.height)};
  }

  function getCell(e){
    const p=toCanvas(e);
    return{x:Math.floor(p.x/S),y:Math.floor(p.y/S)};
  }

  canvas.addEventListener('mousedown',e=>{
    const c=getCell(e);
    if(c.x>=0&&c.x<COLS&&c.y>=0&&c.y<ROWS){
      paintVal=grid[c.y][c.x]?0:1;
      grid[c.y][c.x]=paintVal;painting=true;
    }
  });
  canvas.addEventListener('mousemove',e=>{
    if(!painting)return;
    const c=getCell(e);
    if(c.x>=0&&c.x<COLS&&c.y>=0&&c.y<ROWS)grid[c.y][c.x]=paintVal;
  });
  canvas.addEventListener('mouseup',()=>{painting=false;});

  document.getElementById('at-clear').onclick=()=>{
    for(let y=0;y<ROWS;y++)for(let x=0;x<COLS;x++)grid[y][x]=0;
  };
  document.getElementById('at-fill').onclick=()=>{
    for(let y=0;y<ROWS;y++)for(let x=0;x<COLS;x++)grid[y][x]=Math.random()>0.5?1:0;
  };

  function getBitmask(x,y){
    if(!grid[y][x])return-1;
    let mask=0;
    if(y>0&&grid[y-1][x])mask|=1;        // up
    if(x<COLS-1&&grid[y][x+1])mask|=2;    // right
    if(y<ROWS-1&&grid[y+1][x])mask|=4;    // down
    if(x>0&&grid[y][x-1])mask|=8;         // left
    return mask;
  }

  // Visual representation of each bitmask
  const colors=['#ff6a35','#e85d30','#d5522b','#c24726','#b03c21','#9d311c','#8a2617','#771b12',
                '#ff8050','#ff9060','#ffa070','#ffb080','#ffc090','#ffd0a0','#ffe0b0','#fff0c0'];

  function draw(){
    ctx.fillStyle='#1a1a2e';ctx.fillRect(0,0,canvas.width,canvas.height);

    for(let y=0;y<ROWS;y++)for(let x=0;x<COLS;x++){
      const bm=getBitmask(x,y);
      if(bm>=0){
        ctx.fillStyle=colors[bm];
        ctx.fillRect(x*S+1,y*S+1,S-2,S-2);

        // Draw connection lines based on bitmask
        ctx.strokeStyle='#1a1a2e';ctx.lineWidth=2;
        if(!(bm&1)){ctx.beginPath();ctx.moveTo(x*S+2,y*S+1);ctx.lineTo(x*S+S-2,y*S+1);ctx.stroke();}
        if(!(bm&2)){ctx.beginPath();ctx.moveTo(x*S+S-1,y*S+2);ctx.lineTo(x*S+S-1,y*S+S-2);ctx.stroke();}
        if(!(bm&4)){ctx.beginPath();ctx.moveTo(x*S+2,y*S+S-1);ctx.lineTo(x*S+S-2,y*S+S-1);ctx.stroke();}
        if(!(bm&8)){ctx.beginPath();ctx.moveTo(x*S+1,y*S+2);ctx.lineTo(x*S+1,y*S+S-2);ctx.stroke();}

        ctx.fillStyle='#1a1a2e';ctx.font='bold 11px monospace';ctx.textAlign='center';
        ctx.fillText(bm.toString(),x*S+S/2,y*S+S/2+4);
      }else{
        ctx.strokeStyle='#2a2a3e';ctx.lineWidth=1;
        ctx.strokeRect(x*S,y*S,S,S);
      }
    }
    ctx.textAlign='left';

    requestAnimationFrame(draw);
  }
  draw();
})();
</script>

---

# Scene Transitions

---

## Transition Effects

Fade, wipe, circle iris, and pixelate transitions between two colored scenes.

<div id="transition-demo" style="background:#1a1a2e;border-radius:8px;padding:1rem;margin:1rem 0">
<canvas id="transition-canvas" width="760" height="350" style="width:100%;border-radius:4px"></canvas>
<div style="display:flex;gap:8px;margin-top:8px;flex-wrap:wrap">
<button class="tr-btn" data-type="fade" style="padding:4px 16px;background:#ff6a35;color:#fff;border:none;border-radius:4px;cursor:pointer;font-family:monospace">Fade</button>
<button class="tr-btn" data-type="wipe" style="padding:4px 16px;background:#2a2a3e;color:#eee;border:1px solid #444;border-radius:4px;cursor:pointer;font-family:monospace">Wipe</button>
<button class="tr-btn" data-type="iris" style="padding:4px 16px;background:#2a2a3e;color:#eee;border:1px solid #444;border-radius:4px;cursor:pointer;font-family:monospace">Circle Iris</button>
<button class="tr-btn" data-type="pixelate" style="padding:4px 16px;background:#2a2a3e;color:#eee;border:1px solid #444;border-radius:4px;cursor:pointer;font-family:monospace">Pixelate</button>
</div>
</div>

<script>
(function(){
  const canvas = document.getElementById('transition-canvas');
  const ctx = canvas.getContext('2d');
  const W=760,H=350;
  let scene=0, transitioning=false, transT=0, transType='fade';

  const scenes=[
    {bg:'#1a3a5e',label:'Scene A',items:()=>{
      ctx.fillStyle='#2a5a8e';for(let i=0;i<5;i++)ctx.fillRect(60+i*140,120,80,80);
      ctx.fillStyle='#ff6a35';ctx.beginPath();ctx.arc(380,200,30,0,Math.PI*2);ctx.fill();
    }},
    {bg:'#3a1a2e',label:'Scene B',items:()=>{
      ctx.fillStyle='#6a2a4e';for(let i=0;i<4;i++){ctx.beginPath();ctx.arc(120+i*180,180,40,0,Math.PI*2);ctx.fill();}
      ctx.fillStyle='#4ade80';ctx.fillRect(320,140,120,120);
    }}
  ];

  function drawScene(idx){
    const s=scenes[idx];
    ctx.fillStyle=s.bg;ctx.fillRect(0,0,W,H);
    s.items();
    ctx.fillStyle='#eee';ctx.font='bold 20px monospace';ctx.fillText(s.label,20,40);
  }

  document.querySelectorAll('.tr-btn').forEach(btn=>{
    btn.onclick=()=>{
      if(transitioning)return;
      transType=btn.dataset.type;transitioning=true;transT=0;
      document.querySelectorAll('.tr-btn').forEach(b=>{b.style.background='#2a2a3e';b.style.border='1px solid #444';});
      btn.style.background='#ff6a35';btn.style.border='none';
    };
  });

  function draw(){
    const from=scene,to=1-scene;
    if(transitioning){
      transT+=0.015;
      if(transT>=1){transitioning=false;scene=to;transT=0;}
    }

    if(!transitioning){
      drawScene(scene);
    }else{
      const t=transT;
      if(transType==='fade'){
        drawScene(from);
        ctx.globalAlpha=t;drawScene(to);ctx.globalAlpha=1;
      }else if(transType==='wipe'){
        drawScene(to);
        ctx.save();ctx.beginPath();ctx.rect(0,0,W*(1-t),H);ctx.clip();
        drawScene(from);ctx.restore();
      }else if(transType==='iris'){
        const maxR=Math.sqrt(W*W+H*H)/2;
        if(t<0.5){
          drawScene(from);
          ctx.fillStyle='#000';ctx.beginPath();
          ctx.arc(W/2,H/2,maxR*(1-t*2),0,Math.PI*2);
          ctx.rect(W,0,-W,H);ctx.fill('evenodd');
        }else{
          drawScene(to);
          ctx.fillStyle='#000';ctx.beginPath();
          ctx.arc(W/2,H/2,maxR*((t-0.5)*2),0,Math.PI*2);
          ctx.rect(W,0,-W,H);ctx.fill('evenodd');
        }
      }else if(transType==='pixelate'){
        const maxBlock=40;
        const block=t<0.5?Math.max(1,Math.floor(t*2*maxBlock)):Math.max(1,Math.floor((1-t)*2*maxBlock));
        const srcScene=t<0.5?from:to;
        drawScene(srcScene);
        const imgData=ctx.getImageData(0,0,W,H);
        for(let y=0;y<H;y+=block)for(let x=0;x<W;x+=block){
          const i=(y*W+x)*4;
          ctx.fillStyle=`rgb(${imgData.data[i]},${imgData.data[i+1]},${imgData.data[i+2]})`;
          ctx.fillRect(x,y,block,block);
        }
      }
    }

    requestAnimationFrame(draw);
  }
  draw();
})();
</script>

---

# Finite State Machine

---

## Visual State Machine

A character with Idle→Walk→Run→Jump states. Arrow keys trigger transitions. The current state is highlighted in the diagram.

<div id="fsm-demo" style="background:#1a1a2e;border-radius:8px;padding:1rem;margin:1rem 0">
<canvas id="fsm-canvas" width="760" height="420" style="width:100%;border-radius:4px;outline:none" tabindex="0"></canvas>
<p style="color:#888;font-size:12px;margin:8px 0 0">Click canvas first · Left/Right: Walk · Shift+Arrow: Run · Up: Jump · Release: Idle</p>
</div>

<script>
(function(){
  const canvas = document.getElementById('fsm-canvas');
  const ctx = canvas.getContext('2d');
  const W=760,H=420;
  let state='Idle',stateTime=0;
  const keys={};

  const states={
    Idle:{x:150,y:100,color:'#4ade80'},
    Walk:{x:380,y:100,color:'#60a5fa'},
    Run:{x:610,y:100,color:'#f97316'},
    Jump:{x:380,y:250,color:'#a78bfa'}
  };

  const transitions=[
    {from:'Idle',to:'Walk',label:'Arrow'},
    {from:'Walk',to:'Idle',label:'Release'},
    {from:'Walk',to:'Run',label:'Shift+Arrow'},
    {from:'Run',to:'Walk',label:'Release Shift'},
    {from:'Idle',to:'Jump',label:'Up'},
    {from:'Walk',to:'Jump',label:'Up'},
    {from:'Run',to:'Jump',label:'Up'},
    {from:'Jump',to:'Idle',label:'Land'},
  ];

  let charX=W/2,charY=350,charVY=0,grounded=true;

  canvas.addEventListener('keydown',e=>{keys[e.key]=true;e.preventDefault();});
  canvas.addEventListener('keyup',e=>{keys[e.key]=false;});
  canvas.addEventListener('click',()=>canvas.focus());

  function update(){
    stateTime++;
    const moving=keys['ArrowLeft']||keys['ArrowRight'];
    const running=keys['Shift']&&moving;

    // State transitions
    if(state==='Idle'){
      if(keys['ArrowUp']&&grounded){state='Jump';charVY=-11;grounded=false;stateTime=0;}
      else if(moving){state=running?'Run':'Walk';stateTime=0;}
    }else if(state==='Walk'){
      if(keys['ArrowUp']&&grounded){state='Jump';charVY=-11;grounded=false;stateTime=0;}
      else if(running){state='Run';stateTime=0;}
      else if(!moving){state='Idle';stateTime=0;}
    }else if(state==='Run'){
      if(keys['ArrowUp']&&grounded){state='Jump';charVY=-11;grounded=false;stateTime=0;}
      else if(!running&&moving){state='Walk';stateTime=0;}
      else if(!moving){state='Idle';stateTime=0;}
    }else if(state==='Jump'){
      if(grounded){state=moving?(running?'Run':'Walk'):'Idle';stateTime=0;}
    }

    // Physics
    const spd=state==='Run'?4:state==='Walk'?2:0;
    if(keys['ArrowLeft'])charX-=spd;
    if(keys['ArrowRight'])charX+=spd;
    if(!grounded){charVY+=0.5;charY+=charVY;}
    if(charY>=350){charY=350;charVY=0;grounded=true;}
    if(charX<20)charX=20;if(charX>W-20)charX=W-20;

    // Draw
    ctx.fillStyle='#1a1a2e';ctx.fillRect(0,0,W,H);

    // State diagram
    Object.entries(states).forEach(([name,s])=>{
      const active=name===state;
      ctx.beginPath();ctx.arc(s.x,s.y,35,0,Math.PI*2);
      ctx.fillStyle=active?s.color+'33':'#2a2a3e';ctx.fill();
      ctx.strokeStyle=active?s.color:'#444';ctx.lineWidth=active?3:1;ctx.stroke();
      ctx.fillStyle=active?s.color:'#888';ctx.font=(active?'bold ':'')+' 13px monospace';
      ctx.textAlign='center';ctx.fillText(name,s.x,s.y+4);
    });

    // Transition arrows
    transitions.forEach(t=>{
      const from=states[t.from],to=states[t.to];
      const dx=to.x-from.x,dy=to.y-from.y;
      const dist=Math.sqrt(dx*dx+dy*dy);
      const nx=dx/dist,ny=dy/dist;
      const sx=from.x+nx*38,sy=from.y+ny*38;
      const ex=to.x-nx*38,ey=to.y-ny*38;

      const active=t.from===state;
      ctx.strokeStyle=active?'#ff6a3588':'#333';ctx.lineWidth=1;
      ctx.beginPath();ctx.moveTo(sx,sy);ctx.lineTo(ex,ey);ctx.stroke();

      // Arrowhead
      const ang=Math.atan2(ey-sy,ex-sx);
      ctx.beginPath();
      ctx.moveTo(ex,ey);
      ctx.lineTo(ex-Math.cos(ang-0.3)*8,ey-Math.sin(ang-0.3)*8);
      ctx.moveTo(ex,ey);
      ctx.lineTo(ex-Math.cos(ang+0.3)*8,ey-Math.sin(ang+0.3)*8);
      ctx.stroke();

      // Label
      ctx.fillStyle='#555';ctx.font='9px monospace';
      ctx.fillText(t.label,(sx+ex)/2+ny*12,(sy+ey)/2-nx*12);
    });

    ctx.textAlign='left';

    // Ground
    ctx.fillStyle='#3a3a5e';ctx.fillRect(0,366,W,54);

    // Character
    const bob=state==='Walk'?Math.sin(stateTime*0.2)*3:state==='Run'?Math.sin(stateTime*0.35)*4:Math.sin(stateTime*0.05)*1;
    ctx.fillStyle=states[state].color;
    ctx.fillRect(charX-10,charY-20+bob,20,20);
    ctx.fillStyle='#fff3';ctx.fillRect(charX-6,charY-17+bob,5,5);

    ctx.fillStyle=states[state].color;ctx.font='bold 14px monospace';
    ctx.fillText(`State: ${state}`,10,H-10);
    ctx.fillStyle='#888';ctx.font='11px monospace';
    ctx.fillText(`Time in state: ${stateTime} frames`,200,H-10);

    requestAnimationFrame(update);
  }
  update();
  canvas.focus();
})();
</script>

---

# Fog of War

---

## Recursive Shadowcasting

Click to place/remove walls on the grid. The player follows your mouse. Tiles have three visibility states: unexplored (black), explored (dim), and visible (lit).

<div id="fow-demo" style="background:#1a1a2e;border-radius:8px;padding:1rem;margin:1rem 0">
<canvas id="fow-canvas" width="760" height="400" style="width:100%;border-radius:4px;cursor:crosshair;image-rendering:pixelated"></canvas>
<div style="display:flex;gap:1rem;margin-top:8px;flex-wrap:wrap">
<label style="color:#888;font-size:12px">View radius: <input id="fow-rad" type="range" min="2" max="15" value="7" style="width:100px"><span id="fow-rad-val">7</span></label>
<button id="fow-clear" style="padding:4px 16px;background:#2a2a3e;color:#eee;border:1px solid #444;border-radius:4px;cursor:pointer;font-family:monospace">Clear walls</button>
</div>
<p style="color:#888;font-size:12px;margin:8px 0 0">Click to toggle walls · Mouse controls player position</p>
</div>

<script>
(function(){
  const canvas = document.getElementById('fow-canvas');
  const ctx = canvas.getContext('2d');
  const COLS=38,ROWS=20,S=20;
  const walls=Array.from({length:ROWS},()=>Array(COLS).fill(false));
  const explored=Array.from({length:ROWS},()=>Array(COLS).fill(false));
  let visible=Array.from({length:ROWS},()=>Array(COLS).fill(false));
  let px=COLS/2|0,py=ROWS/2|0;

  document.getElementById('fow-rad').oninput=function(){document.getElementById('fow-rad-val').textContent=this.value;};
  document.getElementById('fow-clear').onclick=()=>{for(let y=0;y<ROWS;y++)for(let x=0;x<COLS;x++)walls[y][x]=false;};

  function toCanvas(e){
    const r=canvas.getBoundingClientRect();
    return{x:(e.clientX-r.left)*(canvas.width/r.width),y:(e.clientY-r.top)*(canvas.height/r.height)};
  }

  canvas.addEventListener('click',e=>{
    const p=toCanvas(e);
    const gx=Math.floor(p.x/S),gy=Math.floor(p.y/S);
    if(gx>=0&&gx<COLS&&gy>=0&&gy<ROWS)walls[gy][gx]=!walls[gy][gx];
  });

  canvas.addEventListener('mousemove',e=>{
    const p=toCanvas(e);
    const gx=Math.floor(p.x/S),gy=Math.floor(p.y/S);
    if(gx>=0&&gx<COLS&&gy>=0&&gy<ROWS&&!walls[gy][gx]){px=gx;py=gy;}
  });

  // Simple raycasting FOV
  function computeFOV(){
    const radius=parseInt(document.getElementById('fow-rad').value);
    visible=Array.from({length:ROWS},()=>Array(COLS).fill(false));
    visible[py][px]=true;explored[py][px]=true;

    for(let angle=0;angle<360;angle+=1){
      const rad=angle*Math.PI/180;
      const dx=Math.cos(rad),dy=Math.sin(rad);
      let rx=px+0.5,ry=py+0.5;
      for(let i=0;i<radius;i++){
        rx+=dx*0.5;ry+=dy*0.5;
        const gx=Math.floor(rx),gy=Math.floor(ry);
        if(gx<0||gy<0||gx>=COLS||gy>=ROWS)break;
        visible[gy][gx]=true;explored[gy][gx]=true;
        if(walls[gy][gx])break;
      }
    }
  }

  function draw(){
    computeFOV();
    ctx.fillStyle='#1a1a2e';ctx.fillRect(0,0,canvas.width,canvas.height);

    for(let y=0;y<ROWS;y++)for(let x=0;x<COLS;x++){
      const vis=visible[y][x],exp=explored[y][x];
      if(!exp){
        ctx.fillStyle='#000';ctx.fillRect(x*S,y*S,S,S);
      }else if(!vis){
        ctx.fillStyle=walls[y][x]?'#1a1a2a':'#111118';
        ctx.fillRect(x*S,y*S,S,S);
        if(walls[y][x]){ctx.strokeStyle='#222';ctx.strokeRect(x*S+1,y*S+1,S-2,S-2);}
      }else{
        if(walls[y][x]){
          ctx.fillStyle='#4a4a6e';ctx.fillRect(x*S,y*S,S,S);
          ctx.strokeStyle='#5a5a7e';ctx.strokeRect(x*S+1,y*S+1,S-2,S-2);
        }else{
          ctx.fillStyle='#2a2a3e';ctx.fillRect(x*S,y*S,S,S);
          ctx.strokeStyle='#333';ctx.strokeRect(x*S,y*S,S,S);
        }
      }
    }

    // Player
    ctx.fillStyle='#ff6a35';ctx.beginPath();ctx.arc(px*S+S/2,py*S+S/2,S/3,0,Math.PI*2);ctx.fill();
    ctx.fillStyle='#ffa07a';ctx.beginPath();ctx.arc(px*S+S/2-2,py*S+S/2-2,S/6,0,Math.PI*2);ctx.fill();

    requestAnimationFrame(draw);
  }
  draw();
})();
</script>

---

# Flow Field

---

## Flow Field Pathfinding

Click to set target (green). Drag to add walls. Arrows show the direction to the target from each cell using BFS-based flow field.

<div id="flow-demo" style="background:#1a1a2e;border-radius:8px;padding:1rem;margin:1rem 0">
<canvas id="flow-canvas" width="760" height="400" style="width:100%;border-radius:4px;cursor:crosshair;image-rendering:pixelated"></canvas>
<div style="display:flex;gap:8px;margin-top:8px">
<button id="flow-clear" style="padding:4px 16px;background:#2a2a3e;color:#eee;border:1px solid #444;border-radius:4px;cursor:pointer;font-family:monospace">Clear Walls</button>
</div>
<p style="color:#888;font-size:12px;margin:8px 0 0">Left click: set target · Right drag: add walls</p>
</div>

<script>
(function(){
  const canvas = document.getElementById('flow-canvas');
  const ctx = canvas.getContext('2d');
  const COLS=38,ROWS=20,S=20;
  const walls=Array.from({length:ROWS},()=>Array(COLS).fill(false));
  const flow=Array.from({length:ROWS},()=>Array(COLS).fill(null));
  const cost=Array.from({length:ROWS},()=>Array(COLS).fill(Infinity));
  let target={x:COLS-2,y:ROWS-2};
  let drawingWalls=false;

  function toCell(e){
    const r=canvas.getBoundingClientRect();
    const x=Math.floor((e.clientX-r.left)*(canvas.width/r.width)/S);
    const y=Math.floor((e.clientY-r.top)*(canvas.height/r.height)/S);
    return{x,y};
  }

  canvas.addEventListener('click',e=>{
    const c=toCell(e);
    if(c.x>=0&&c.x<COLS&&c.y>=0&&c.y<ROWS&&!walls[c.y][c.x]){
      target=c;computeFlow();
    }
  });
  canvas.addEventListener('contextmenu',e=>{e.preventDefault();});
  canvas.addEventListener('mousedown',e=>{
    if(e.button===2){drawingWalls=true;const c=toCell(e);if(c.x>=0&&c.x<COLS&&c.y>=0&&c.y<ROWS){walls[c.y][c.x]=true;computeFlow();}}
  });
  canvas.addEventListener('mousemove',e=>{
    if(!drawingWalls)return;
    const c=toCell(e);
    if(c.x>=0&&c.x<COLS&&c.y>=0&&c.y<ROWS){walls[c.y][c.x]=true;computeFlow();}
  });
  canvas.addEventListener('mouseup',()=>{drawingWalls=false;});

  document.getElementById('flow-clear').onclick=()=>{
    for(let y=0;y<ROWS;y++)for(let x=0;x<COLS;x++)walls[y][x]=false;
    computeFlow();
  };

  function computeFlow(){
    for(let y=0;y<ROWS;y++)for(let x=0;x<COLS;x++){cost[y][x]=Infinity;flow[y][x]=null;}
    const queue=[target];
    cost[target.y][target.x]=0;

    while(queue.length){
      const cur=queue.shift();
      for(const[dx,dy]of[[0,-1],[1,0],[0,1],[-1,0]]){
        const nx=cur.x+dx,ny=cur.y+dy;
        if(nx<0||ny<0||nx>=COLS||ny>=ROWS||walls[ny][nx])continue;
        const nc=cost[cur.y][cur.x]+1;
        if(nc<cost[ny][nx]){
          cost[ny][nx]=nc;
          flow[ny][nx]={x:-dx,y:-dy}; // direction TOWARD target
          queue.push({x:nx,y:ny});
        }
      }
    }
  }

  computeFlow();

  function draw(){
    ctx.fillStyle='#1a1a2e';ctx.fillRect(0,0,canvas.width,canvas.height);

    const maxCost=Math.max(...cost.flat().filter(c=>c<Infinity),1);

    for(let y=0;y<ROWS;y++)for(let x=0;x<COLS;x++){
      if(walls[y][x]){
        ctx.fillStyle='#444';ctx.fillRect(x*S+1,y*S+1,S-2,S-2);
      }else{
        const c=cost[y][x];
        if(c<Infinity){
          const t=1-c/maxCost;
          ctx.fillStyle=`rgba(255,106,53,${t*0.15})`;
          ctx.fillRect(x*S,y*S,S,S);
        }
        ctx.strokeStyle='#2a2a3e';ctx.strokeRect(x*S,y*S,S,S);

        // Arrow
        const f=flow[y][x];
        if(f){
          const cx2=x*S+S/2,cy2=y*S+S/2;
          const ax=f.x*6,ay=f.y*6;
          ctx.strokeStyle=`rgba(255,106,53,${0.3+0.4*(1-c/maxCost)})`;
          ctx.lineWidth=1.5;
          ctx.beginPath();ctx.moveTo(cx2-ax,cy2-ay);ctx.lineTo(cx2+ax,cy2+ay);ctx.stroke();
          // Arrowhead
          const ang=Math.atan2(ay,ax);
          ctx.beginPath();
          ctx.moveTo(cx2+ax,cy2+ay);
          ctx.lineTo(cx2+ax-Math.cos(ang-0.5)*5,cy2+ay-Math.sin(ang-0.5)*5);
          ctx.moveTo(cx2+ax,cy2+ay);
          ctx.lineTo(cx2+ax-Math.cos(ang+0.5)*5,cy2+ay-Math.sin(ang+0.5)*5);
          ctx.stroke();
        }
      }
    }

    // Target
    ctx.fillStyle='#4ade80';
    ctx.beginPath();ctx.arc(target.x*S+S/2,target.y*S+S/2,6,0,Math.PI*2);ctx.fill();

    requestAnimationFrame(draw);
  }
  draw();
})();
</script>

---

# Game Feel / Juice

---

## Comprehensive Juice Demo

Press the button to trigger a hit with screen shake, hitstop (freeze frames), and squash-stretch on the target. Adjust parameters.

<div id="juice-demo" style="background:#1a1a2e;border-radius:8px;padding:1rem;margin:1rem 0">
<canvas id="juice-canvas" width="760" height="350" style="width:100%;border-radius:4px"></canvas>
<div style="display:flex;gap:1rem;margin-top:8px;flex-wrap:wrap;align-items:center">
<button id="juice-hit" style="padding:6px 24px;background:#ff6a35;color:#fff;border:none;border-radius:4px;cursor:pointer;font-family:monospace;font-size:14px">💥 HIT!</button>
<label style="color:#888;font-size:12px">Shake: <input id="juice-shake" type="range" min="0" max="25" value="12" style="width:80px"></label>
<label style="color:#888;font-size:12px">Hitstop: <input id="juice-hitstop" type="range" min="0" max="20" value="6" style="width:80px"></label>
<label style="color:#888;font-size:12px">Squash: <input id="juice-squash" type="range" min="0" max="100" value="50" style="width:80px"></label>
</div>
</div>

<script>
(function(){
  const canvas = document.getElementById('juice-canvas');
  const ctx = canvas.getContext('2d');
  const W=760,H=350;
  let shakeTrauma=0, hitstopFrames=0, squashT=0;
  let targetScale={x:1,y:1},targetVel={x:0,y:0};
  let flashAlpha=0;
  const particles=[];

  document.getElementById('juice-hit').onclick=()=>{
    const shake=parseInt(document.getElementById('juice-shake').value);
    const hitstop=parseInt(document.getElementById('juice-hitstop').value);
    const squash=parseInt(document.getElementById('juice-squash').value)/100;

    shakeTrauma=1;
    hitstopFrames=hitstop;
    targetScale={x:1+squash*0.5,y:1-squash*0.3};
    flashAlpha=1;

    // Particles
    for(let i=0;i<20;i++){
      const ang=Math.random()*Math.PI*2;
      const spd=2+Math.random()*5;
      particles.push({x:W/2,y:H/2,vx:Math.cos(ang)*spd,vy:Math.sin(ang)*spd,life:20+Math.random()*20,maxLife:40,size:3+Math.random()*4});
    }
  };

  function draw(){
    if(hitstopFrames>0){hitstopFrames--;
      // During hitstop, just redraw frozen frame with white flash
      ctx.fillStyle=`rgba(255,255,255,${flashAlpha*0.3})`;ctx.fillRect(0,0,W,H);
      flashAlpha*=0.85;
      requestAnimationFrame(draw);return;
    }

    const shakeInt=parseInt(document.getElementById('juice-shake').value);
    let sx=0,sy=0;
    if(shakeTrauma>0.01){
      sx=(Math.random()*2-1)*shakeTrauma*shakeInt;
      sy=(Math.random()*2-1)*shakeTrauma*shakeInt;
      shakeTrauma*=0.9;
    }else shakeTrauma=0;

    // Squash recovery (spring)
    targetScale.x+=(1-targetScale.x)*0.15;
    targetScale.y+=(1-targetScale.y)*0.15;
    flashAlpha*=0.9;

    ctx.fillStyle='#1a1a2e';ctx.fillRect(0,0,W,H);
    ctx.save();ctx.translate(sx,sy);

    // Background elements
    ctx.fillStyle='#2a2a3e';ctx.fillRect(0,H-50,W,50);
    for(let i=0;i<8;i++){
      ctx.fillStyle='#333';ctx.fillRect(50+i*90,H-50-30-Math.random()*2,50,30);
    }

    // Target with squash-stretch
    ctx.save();
    ctx.translate(W/2,H/2);
    ctx.scale(targetScale.x,targetScale.y);
    ctx.fillStyle='#ff6a35';ctx.fillRect(-30,-30,60,60);
    ctx.strokeStyle='#ffa07a';ctx.lineWidth=2;ctx.strokeRect(-30,-30,60,60);
    ctx.fillStyle='#fff';ctx.font='20px monospace';ctx.textAlign='center';
    ctx.fillText('🎯',0,8);
    ctx.restore();

    // Particles
    for(let i=particles.length-1;i>=0;i--){
      const p=particles[i];
      p.x+=p.vx;p.y+=p.vy;p.vy+=0.1;p.life--;
      const a=p.life/p.maxLife;
      ctx.fillStyle=`rgba(255,106,53,${a})`;
      ctx.fillRect(p.x-p.size/2,p.y-p.size/2,p.size*a,p.size*a);
      if(p.life<=0)particles.splice(i,1);
    }

    // White flash overlay
    if(flashAlpha>0.01){
      ctx.fillStyle=`rgba(255,255,255,${flashAlpha*0.2})`;
      ctx.fillRect(0,0,W,H);
    }

    ctx.restore();
    ctx.textAlign='left';

    // HUD
    ctx.fillStyle='#888';ctx.font='11px monospace';
    ctx.fillText(`Shake: ${shakeTrauma.toFixed(2)}  Scale: ${targetScale.x.toFixed(2)}x${targetScale.y.toFixed(2)}`,10,20);

    requestAnimationFrame(draw);
  }
  draw();
})();
</script>

---

# Procedural Generation

---

## BSP Dungeon Generator

Binary Space Partitioning generates rooms and corridors. Adjust minimum room size and regenerate.

<div id="bsp-demo" style="background:#1a1a2e;border-radius:8px;padding:1rem;margin:1rem 0">
<canvas id="bsp-canvas" width="760" height="400" style="width:100%;border-radius:4px;image-rendering:pixelated"></canvas>
<div style="display:flex;gap:1rem;margin-top:8px;align-items:center;flex-wrap:wrap">
<button id="bsp-gen" style="padding:4px 16px;background:#ff6a35;color:#fff;border:none;border-radius:4px;cursor:pointer;font-family:monospace">Generate</button>
<label style="color:#888;font-size:12px">Min room: <input id="bsp-min" type="range" min="3" max="10" value="5" style="width:80px"><span id="bsp-min-val">5</span></label>
<label style="color:#888;font-size:12px">Depth: <input id="bsp-depth" type="range" min="2" max="6" value="4" style="width:80px"><span id="bsp-depth-val">4</span></label>
</div>
</div>

<script>
(function(){
  const canvas = document.getElementById('bsp-canvas');
  const ctx = canvas.getContext('2d');
  const W=760,H=400,S=8;
  const COLS=W/S|0,ROWS=H/S|0;

  document.getElementById('bsp-min').oninput=function(){document.getElementById('bsp-min-val').textContent=this.value;};
  document.getElementById('bsp-depth').oninput=function(){document.getElementById('bsp-depth-val').textContent=this.value;};

  function split(node,depth,minSize){
    if(depth<=0||node.w<minSize*2+2||node.h<minSize*2+2)return;
    const horiz=node.w<node.h?true:node.h<node.w?false:Math.random()>0.5;
    if(horiz){
      const splitY=minSize+Math.floor(Math.random()*(node.h-minSize*2));
      node.a={x:node.x,y:node.y,w:node.w,h:splitY};
      node.b={x:node.x,y:node.y+splitY,w:node.w,h:node.h-splitY};
    }else{
      const splitX=minSize+Math.floor(Math.random()*(node.w-minSize*2));
      node.a={x:node.x,y:node.y,w:splitX,h:node.h};
      node.b={x:node.x+splitX,y:node.y,w:node.w-splitX,h:node.h};
    }
    split(node.a,depth-1,minSize);
    split(node.b,depth-1,minSize);
  }

  function getLeaves(node){
    if(!node.a&&!node.b)return[node];
    return[...getLeaves(node.a),...getLeaves(node.b)];
  }

  function roomCenter(node){
    if(node.room)return{x:node.room.x+node.room.w/2|0,y:node.room.y+node.room.h/2|0};
    if(!node.a)return{x:node.x+node.w/2|0,y:node.y+node.h/2|0};
    const ca=roomCenter(node.a),cb=roomCenter(node.b);
    return{x:(ca.x+cb.x)/2|0,y:(ca.y+cb.y)/2|0};
  }

  function generate(){
    const minRoom=parseInt(document.getElementById('bsp-min').value);
    const depth=parseInt(document.getElementById('bsp-depth').value);
    const grid=Array.from({length:ROWS},()=>Array(COLS).fill(1));

    const root={x:1,y:1,w:COLS-2,h:ROWS-2};
    split(root,depth,minRoom);

    const leaves=getLeaves(root);
    leaves.forEach(l=>{
      const rw=minRoom+Math.floor(Math.random()*(l.w-minRoom-1));
      const rh=minRoom+Math.floor(Math.random()*(l.h-minRoom-1));
      const rx=l.x+Math.floor(Math.random()*(l.w-rw));
      const ry=l.y+Math.floor(Math.random()*(l.h-rh));
      l.room={x:rx,y:ry,w:rw,h:rh};
      for(let y=ry;y<ry+rh&&y<ROWS;y++)for(let x=rx;x<rx+rw&&x<COLS;x++)grid[y][x]=0;
    });

    // Connect siblings
    function connect(node){
      if(!node.a||!node.b)return;
      connect(node.a);connect(node.b);
      const ca=roomCenter(node.a),cb=roomCenter(node.b);
      let x=ca.x,y=ca.y;
      while(x!==cb.x){x+=x<cb.x?1:-1;if(y>=0&&y<ROWS&&x>=0&&x<COLS)grid[y][x]=0;if(y+1<ROWS)grid[y+1][x]=0;}
      while(y!==cb.y){y+=y<cb.y?1:-1;if(y>=0&&y<ROWS&&x>=0&&x<COLS)grid[y][x]=0;if(x+1<COLS)grid[y][x+1]=0;}
    }
    connect(root);

    // Draw
    ctx.fillStyle='#1a1a2e';ctx.fillRect(0,0,W,H);
    for(let y=0;y<ROWS;y++)for(let x=0;x<COLS;x++){
      if(grid[y][x]){ctx.fillStyle='#3a3a5e';ctx.fillRect(x*S,y*S,S,S);}
      else{ctx.fillStyle='#8b6914';ctx.fillRect(x*S,y*S,S,S);ctx.fillStyle='#a07818';ctx.fillRect(x*S+1,y*S+1,S-2,S-2);}
    }

    // Show split lines
    function drawSplits(node,d){
      if(!node.a)return;
      ctx.strokeStyle=`hsla(${d*60},70%,60%,0.3)`;ctx.lineWidth=1;ctx.setLineDash([3,3]);
      if(node.a.x===node.b.x){
        const sy=node.b.y*S;ctx.beginPath();ctx.moveTo(node.x*S,sy);ctx.lineTo((node.x+node.w)*S,sy);ctx.stroke();
      }else{
        const sx=node.b.x*S;ctx.beginPath();ctx.moveTo(sx,node.y*S);ctx.lineTo(sx,(node.y+node.h)*S);ctx.stroke();
      }
      ctx.setLineDash([]);
      drawSplits(node.a,d+1);drawSplits(node.b,d+1);
    }
    drawSplits(root,0);
  }

  document.getElementById('bsp-gen').onclick=generate;
  generate();
})();
</script>

---

## Drunkard's Walk

Watch the drunkard carve a cave in real-time. Adjust step count and speed.

<div id="drunk-demo" style="background:#1a1a2e;border-radius:8px;padding:1rem;margin:1rem 0">
<canvas id="drunk-canvas" width="760" height="400" style="width:100%;border-radius:4px;image-rendering:pixelated"></canvas>
<div style="display:flex;gap:1rem;margin-top:8px;align-items:center;flex-wrap:wrap">
<button id="drunk-gen" style="padding:4px 16px;background:#ff6a35;color:#fff;border:none;border-radius:4px;cursor:pointer;font-family:monospace">Start Walk</button>
<label style="color:#888;font-size:12px">Steps: <input id="drunk-steps" type="range" min="500" max="5000" value="2000" style="width:100px"><span id="drunk-steps-val">2000</span></label>
<label style="color:#888;font-size:12px">Speed: <input id="drunk-speed" type="range" min="1" max="50" value="10" style="width:80px"><span id="drunk-speed-val">10</span></label>
</div>
</div>

<script>
(function(){
  const canvas = document.getElementById('drunk-canvas');
  const ctx = canvas.getContext('2d');
  const S=8,COLS=760/S|0,ROWS=400/S|0;
  let grid,wx,wy,stepsLeft,animId;

  document.getElementById('drunk-steps').oninput=function(){document.getElementById('drunk-steps-val').textContent=this.value;};
  document.getElementById('drunk-speed').oninput=function(){document.getElementById('drunk-speed-val').textContent=this.value;};

  function init(){
    if(animId)cancelAnimationFrame(animId);
    grid=Array.from({length:ROWS},()=>Array(COLS).fill(1));
    wx=COLS/2|0;wy=ROWS/2|0;
    stepsLeft=parseInt(document.getElementById('drunk-steps').value);
    grid[wy][wx]=0;
    drawGrid();
    animate();
  }

  function drawGrid(){
    ctx.fillStyle='#1a1a2e';ctx.fillRect(0,0,760,400);
    for(let y=0;y<ROWS;y++)for(let x=0;x<COLS;x++){
      if(grid[y][x]){ctx.fillStyle='#3a3a5e';ctx.fillRect(x*S,y*S,S,S);}
      else{ctx.fillStyle='#a07818';ctx.fillRect(x*S,y*S,S,S);}
    }
    // Walker position
    ctx.fillStyle='#ff6a35';ctx.fillRect(wx*S,wy*S,S,S);
  }

  function animate(){
    const speed=parseInt(document.getElementById('drunk-speed').value);
    for(let i=0;i<speed&&stepsLeft>0;i++){
      const dirs=[[0,-1],[1,0],[0,1],[-1,0]];
      const d=dirs[Math.random()*4|0];
      const nx=wx+d[0],ny=wy+d[1];
      if(nx>0&&nx<COLS-1&&ny>0&&ny<ROWS-1){wx=nx;wy=ny;grid[wy][wx]=0;}
      stepsLeft--;
    }
    drawGrid();

    ctx.fillStyle='#888';ctx.font='11px monospace';
    ctx.fillText(`Steps remaining: ${stepsLeft}`,10,390);

    if(stepsLeft>0)animId=requestAnimationFrame(animate);
  }

  document.getElementById('drunk-gen').onclick=init;
  init();
})();
</script>

---

# Physics Simulations

---

## Water Surface Simulation

Click on the water to create splashes. Springs propagate waves across the surface.

<div id="water-demo" style="background:#1a1a2e;border-radius:8px;padding:1rem;margin:1rem 0">
<canvas id="water-canvas" width="760" height="350" style="width:100%;border-radius:4px;cursor:pointer"></canvas>
<div style="display:flex;gap:1rem;margin-top:8px;flex-wrap:wrap">
<label style="color:#888;font-size:12px">Tension: <input id="water-tension" type="range" min="0.005" max="0.08" step="0.005" value="0.025" style="width:100px"></label>
<label style="color:#888;font-size:12px">Dampening: <input id="water-damp" type="range" min="0.9" max="0.999" step="0.001" value="0.975" style="width:100px"></label>
<label style="color:#888;font-size:12px">Spread: <input id="water-spread" type="range" min="0.1" max="0.5" step="0.01" value="0.25" style="width:100px"></label>
</div>
</div>

<script>
(function(){
  const canvas = document.getElementById('water-canvas');
  const ctx = canvas.getContext('2d');
  const W=760,H=350;
  const NUM=150;
  const baseY=H*0.55;
  const springs=[];

  for(let i=0;i<NUM;i++){
    springs.push({height:baseY,velocity:0,x:i*(W/NUM)});
  }

  canvas.addEventListener('click',e=>{
    const r=canvas.getBoundingClientRect();
    const mx=(e.clientX-r.left)*(W/r.width);
    const idx=Math.floor(mx/(W/NUM));
    if(idx>=0&&idx<NUM){
      springs[idx].velocity=15+Math.random()*10;
    }
  });

  canvas.addEventListener('mousemove',e=>{
    if(e.buttons!==1)return;
    const r=canvas.getBoundingClientRect();
    const mx=(e.clientX-r.left)*(W/r.width);
    const idx=Math.floor(mx/(W/NUM));
    if(idx>=0&&idx<NUM)springs[idx].velocity=5;
  });

  function update(){
    const tension=parseFloat(document.getElementById('water-tension').value);
    const dampening=parseFloat(document.getElementById('water-damp').value);
    const spread=parseFloat(document.getElementById('water-spread').value);

    // Update springs
    springs.forEach(s=>{
      const dy=baseY-s.height;
      s.velocity+=tension*dy;
      s.velocity*=dampening;
      s.height+=s.velocity;
    });

    // Propagate
    for(let j=0;j<4;j++){
      const deltas=springs.map(()=>0);
      for(let i=0;i<NUM;i++){
        if(i>0)deltas[i-1]+=spread*(springs[i].height-springs[i-1].height);
        if(i<NUM-1)deltas[i+1]+=spread*(springs[i].height-springs[i+1].height);
      }
      for(let i=0;i<NUM;i++){
        springs[i].velocity+=deltas[i];
        springs[i].height+=deltas[i];
      }
    }

    // Draw
    ctx.fillStyle='#1a1a2e';ctx.fillRect(0,0,W,H);

    // Sky gradient
    const grad=ctx.createLinearGradient(0,0,0,baseY);
    grad.addColorStop(0,'#0a0a1e');grad.addColorStop(1,'#1a2a4e');
    ctx.fillStyle=grad;ctx.fillRect(0,0,W,baseY);

    // Water body
    ctx.beginPath();
    ctx.moveTo(0,H);
    springs.forEach((s,i)=>{
      if(i===0)ctx.lineTo(s.x,s.height);
      else{
        const prev=springs[i-1];
        const cpx=(prev.x+s.x)/2;
        ctx.quadraticCurveTo(prev.x,prev.height,cpx,(prev.height+s.height)/2);
      }
    });
    ctx.lineTo(W,H);ctx.closePath();

    const waterGrad=ctx.createLinearGradient(0,baseY-40,0,H);
    waterGrad.addColorStop(0,'#1a4a8e');waterGrad.addColorStop(0.5,'#123a6e');waterGrad.addColorStop(1,'#0a1a3e');
    ctx.fillStyle=waterGrad;ctx.fill();

    // Surface highlight
    ctx.strokeStyle='rgba(100,180,255,0.4)';ctx.lineWidth=2;
    ctx.beginPath();
    springs.forEach((s,i)=>{
      i===0?ctx.moveTo(s.x,s.height):ctx.lineTo(s.x,s.height);
    });
    ctx.stroke();

    requestAnimationFrame(update);
  }
  update();
})();
</script>

---

## Verlet Rope Simulation

Drag the endpoints. Gravity pulls the rope down. Adjust segment count.

<div id="rope-demo" style="background:#1a1a2e;border-radius:8px;padding:1rem;margin:1rem 0">
<canvas id="rope-canvas" width="760" height="400" style="width:100%;border-radius:4px;cursor:grab"></canvas>
<div style="display:flex;gap:1rem;margin-top:8px;flex-wrap:wrap">
<label style="color:#888;font-size:12px">Segments: <input id="rope-segs" type="range" min="5" max="40" value="20" style="width:100px"><span id="rope-segs-val">20</span></label>
<label style="color:#888;font-size:12px">Gravity: <input id="rope-grav" type="range" min="0" max="2" step="0.1" value="0.5" style="width:100px"></label>
<button id="rope-reset" style="padding:4px 16px;background:#2a2a3e;color:#eee;border:1px solid #444;border-radius:4px;cursor:pointer;font-family:monospace">Reset</button>
</div>
</div>

<script>
(function(){
  const canvas = document.getElementById('rope-canvas');
  const ctx = canvas.getContext('2d');
  const W=760,H=400;
  let points=[],segLen=15,drag=null,ox=0,oy=0;

  document.getElementById('rope-segs').oninput=function(){
    document.getElementById('rope-segs-val').textContent=this.value;
    initRope();
  };
  document.getElementById('rope-reset').onclick=initRope;

  function initRope(){
    const numSegs=parseInt(document.getElementById('rope-segs').value);
    points=[];
    for(let i=0;i<=numSegs;i++){
      const t=i/numSegs;
      points.push({x:150+t*460,y:100,ox:150+t*460,oy:100,pinned:i===0||i===numSegs});
    }
    segLen=460/numSegs;
  }

  function toCanvas(e){
    const r=canvas.getBoundingClientRect();
    return{x:(e.clientX-r.left)*(W/r.width),y:(e.clientY-r.top)*(H/r.height)};
  }

  canvas.addEventListener('mousedown',e=>{
    const p=toCanvas(e);
    for(let i=0;i<points.length;i++){
      const dx=p.x-points[i].x,dy=p.y-points[i].y;
      if(dx*dx+dy*dy<400){drag=i;ox=dx;oy=dy;return;}
    }
  });
  canvas.addEventListener('mousemove',e=>{
    if(drag===null)return;
    const p=toCanvas(e);
    points[drag].x=p.x-ox;points[drag].y=p.y-oy;
  });
  canvas.addEventListener('mouseup',()=>{drag=null;});

  function update(){
    const gravity=parseFloat(document.getElementById('rope-grav').value);

    // Verlet integration
    points.forEach(p=>{
      if(p.pinned||p===points[drag])return;
      const vx=p.x-p.ox,vy=p.y-p.oy;
      p.ox=p.x;p.oy=p.y;
      p.x+=vx*0.99;p.y+=vy*0.99+gravity;
    });

    // Constraints
    for(let iter=0;iter<5;iter++){
      for(let i=0;i<points.length-1;i++){
        const a=points[i],b=points[i+1];
        const dx=b.x-a.x,dy=b.y-a.y;
        const dist=Math.sqrt(dx*dx+dy*dy);
        const diff=(segLen-dist)/dist*0.5;
        const offX=dx*diff,offY=dy*diff;
        if(!a.pinned&&a!==points[drag]){a.x-=offX;a.y-=offY;}
        if(!b.pinned&&b!==points[drag]){b.x+=offX;b.y+=offY;}
      }
    }

    // Draw
    ctx.fillStyle='#1a1a2e';ctx.fillRect(0,0,W,H);

    // Rope
    ctx.strokeStyle='#ff6a35';ctx.lineWidth=3;ctx.beginPath();
    points.forEach((p,i)=>i===0?ctx.moveTo(p.x,p.y):ctx.lineTo(p.x,p.y));
    ctx.stroke();

    // Points
    points.forEach((p,i)=>{
      ctx.fillStyle=p.pinned?'#4ade80':'#ff6a35';
      ctx.beginPath();ctx.arc(p.x,p.y,p.pinned?6:3,0,Math.PI*2);ctx.fill();
    });

    requestAnimationFrame(update);
  }

  initRope();
  update();
})();
</script>

---

# Rendering Techniques

---

## Y-Sort Rendering

Drag sprites around. They automatically sort by Y position for correct depth ordering in top-down games.

<div id="ysort-demo" style="background:#1a1a2e;border-radius:8px;padding:1rem;margin:1rem 0">
<canvas id="ysort-canvas" width="760" height="400" style="width:100%;border-radius:4px;cursor:grab"></canvas>
<p style="color:#888;font-size:12px;margin:8px 0 0">Drag sprites up/down to see Y-sorting in action · Lower = in front</p>
</div>

<script>
(function(){
  const canvas = document.getElementById('ysort-canvas');
  const ctx = canvas.getContext('2d');
  const W=760,H=400;

  const sprites=[
    {x:200,y:180,w:40,h:50,color:'#ff6a35',label:'A',shadow:true},
    {x:350,y:220,w:35,h:55,color:'#4ade80',label:'B',shadow:true},
    {x:300,y:160,w:45,h:45,color:'#60a5fa',label:'C',shadow:true},
    {x:450,y:250,w:38,h:52,color:'#f97316',label:'D',shadow:true},
    {x:500,y:190,w:42,h:48,color:'#a78bfa',label:'E',shadow:true},
    {x:150,y:280,w:36,h:54,color:'#f43f5e',label:'F',shadow:true},
  ];

  let drag=null,ox=0,oy=0;

  function toCanvas(e){
    const r=canvas.getBoundingClientRect();
    return{x:(e.clientX-r.left)*(W/r.width),y:(e.clientY-r.top)*(H/r.height)};
  }

  canvas.addEventListener('mousedown',e=>{
    const p=toCanvas(e);
    // Check in reverse draw order (front to back)
    const sorted=[...sprites].sort((a,b)=>b.y-a.y);
    for(const s of sorted){
      if(p.x>=s.x&&p.x<=s.x+s.w&&p.y>=s.y-s.h&&p.y<=s.y){
        drag=s;ox=p.x-s.x;oy=p.y-s.y;return;
      }
    }
  });
  canvas.addEventListener('mousemove',e=>{
    if(!drag)return;
    const p=toCanvas(e);
    drag.x=p.x-ox;drag.y=p.y-oy;
  });
  canvas.addEventListener('mouseup',()=>{drag=null;});

  function draw(){
    ctx.fillStyle='#1a1a2e';ctx.fillRect(0,0,W,H);

    // Grass texture
    ctx.fillStyle='#1a2a1e';ctx.fillRect(0,0,W,H);
    for(let i=0;i<200;i++){
      ctx.fillStyle=`rgba(30,60,30,${0.3+Math.random()*0.3})`;
      ctx.fillRect((i*37)%W,(i*53)%H,3,3);
    }

    // Y-sort guide lines
    ctx.strokeStyle='#333';ctx.lineWidth=1;ctx.setLineDash([2,4]);
    sprites.forEach(s=>{
      ctx.beginPath();ctx.moveTo(0,s.y);ctx.lineTo(W,s.y);ctx.stroke();
    });
    ctx.setLineDash([]);

    // Sort by Y and draw
    const sorted=[...sprites].sort((a,b)=>a.y-b.y);
    sorted.forEach(s=>{
      // Shadow
      ctx.fillStyle='rgba(0,0,0,0.3)';
      ctx.beginPath();ctx.ellipse(s.x+s.w/2,s.y,s.w/2,6,0,0,Math.PI*2);ctx.fill();

      // Body
      ctx.fillStyle=s.color;
      ctx.fillRect(s.x,s.y-s.h,s.w,s.h);

      // Highlight
      ctx.fillStyle='rgba(255,255,255,0.15)';
      ctx.fillRect(s.x+2,s.y-s.h+2,s.w/3,s.h-4);

      // Label
      ctx.fillStyle='#fff';ctx.font='bold 14px monospace';ctx.textAlign='center';
      ctx.fillText(s.label,s.x+s.w/2,s.y-s.h/2+5);

      // Y value
      ctx.fillStyle='#888';ctx.font='10px monospace';
      ctx.fillText(`y:${Math.round(s.y)}`,s.x+s.w/2,s.y+14);
    });

    ctx.textAlign='left';
    ctx.fillStyle='#ff6a35';ctx.font='12px monospace';
    ctx.fillText('Draw order (back→front): '+sorted.map(s=>s.label).join(' → '),10,20);

    requestAnimationFrame(draw);
  }
  draw();
})();
</script>

---

# Weather Effects

---

## Rain & Snow Particles

Toggle between rain and snow. Adjust wind direction with the slider.

<div id="weather-demo" style="background:#1a1a2e;border-radius:8px;padding:1rem;margin:1rem 0">
<canvas id="weather-canvas" width="760" height="400" style="width:100%;border-radius:4px"></canvas>
<div style="display:flex;gap:1rem;margin-top:8px;flex-wrap:wrap;align-items:center">
<button id="wx-rain" style="padding:4px 16px;background:#ff6a35;color:#fff;border:none;border-radius:4px;cursor:pointer;font-family:monospace">🌧 Rain</button>
<button id="wx-snow" style="padding:4px 16px;background:#2a2a3e;color:#eee;border:1px solid #444;border-radius:4px;cursor:pointer;font-family:monospace">❄ Snow</button>
<label style="color:#888;font-size:12px">Wind: <input id="wx-wind" type="range" min="-5" max="5" step="0.5" value="1" style="width:120px"><span id="wx-wind-val">1</span></label>
</div>
</div>

<script>
(function(){
  const canvas = document.getElementById('weather-canvas');
  const ctx = canvas.getContext('2d');
  const W=760,H=400;
  let mode='rain';
  const particles=[];
  const MAX=400;

  document.getElementById('wx-rain').onclick=function(){
    mode='rain';this.style.background='#ff6a35';this.style.border='none';
    document.getElementById('wx-snow').style.background='#2a2a3e';document.getElementById('wx-snow').style.border='1px solid #444';
  };
  document.getElementById('wx-snow').onclick=function(){
    mode='snow';this.style.background='#ff6a35';this.style.border='none';
    document.getElementById('wx-rain').style.background='#2a2a3e';document.getElementById('wx-rain').style.border='1px solid #444';
  };
  document.getElementById('wx-wind').oninput=function(){document.getElementById('wx-wind-val').textContent=this.value;};

  function spawn(){
    const wind=parseFloat(document.getElementById('wx-wind').value);
    if(mode==='rain'){
      particles.push({x:Math.random()*W,y:-10,vx:wind,vy:8+Math.random()*6,size:1.5,life:1});
    }else{
      particles.push({x:Math.random()*W,y:-10,vx:wind+Math.sin(Date.now()*0.001+Math.random()*10)*0.5,
        vy:0.5+Math.random()*1.5,size:2+Math.random()*3,life:1,wobble:Math.random()*Math.PI*2});
    }
  }

  function draw(){
    ctx.fillStyle='rgba(26,26,46,0.3)';ctx.fillRect(0,0,W,H);

    // Scene
    ctx.fillStyle='#1a2a3e';ctx.fillRect(0,0,W,H);
    ctx.fillStyle='#2a3a2e';ctx.fillRect(0,H-60,W,60);

    // Trees
    for(let i=0;i<5;i++){
      const tx=100+i*150;
      ctx.fillStyle='#3a2a1a';ctx.fillRect(tx-4,H-100,8,40);
      ctx.fillStyle='#1a4a1a';ctx.beginPath();ctx.arc(tx,H-100,25,0,Math.PI*2);ctx.fill();
    }

    const wind=parseFloat(document.getElementById('wx-wind').value);

    // Spawn particles
    const rate=mode==='rain'?8:3;
    for(let i=0;i<rate&&particles.length<MAX;i++)spawn();

    // Update and draw particles
    for(let i=particles.length-1;i>=0;i--){
      const p=particles[i];
      p.vx=wind+(mode==='snow'?Math.sin(Date.now()*0.002+(p.wobble||0))*0.8:0);
      p.x+=p.vx;p.y+=p.vy;

      if(mode==='rain'){
        ctx.strokeStyle='rgba(100,150,255,0.6)';ctx.lineWidth=p.size;
        ctx.beginPath();ctx.moveTo(p.x,p.y);ctx.lineTo(p.x-p.vx*2,p.y-p.vy*2);ctx.stroke();
      }else{
        ctx.fillStyle=`rgba(220,230,255,${0.3+Math.random()*0.4})`;
        ctx.beginPath();ctx.arc(p.x,p.y,p.size,0,Math.PI*2);ctx.fill();
      }

      if(p.y>H||p.x<-20||p.x>W+20)particles.splice(i,1);
    }

    ctx.fillStyle='#888';ctx.font='11px monospace';
    ctx.fillText(`${mode} | particles: ${particles.length} | wind: ${wind}`,10,20);

    requestAnimationFrame(draw);
  }
  draw();
})();
</script>

---

# Minimap

---

## Scrollable World with Minimap

Move your character with WASD/arrows through a large world. The minimap shows your viewport position.

<div id="minimap-demo" style="background:#1a1a2e;border-radius:8px;padding:1rem;margin:1rem 0">
<canvas id="minimap-canvas" width="760" height="400" style="width:100%;border-radius:4px;outline:none" tabindex="0"></canvas>
<p style="color:#888;font-size:12px;margin:8px 0 0">Click canvas first · WASD/arrows to move · Minimap in top-right corner</p>
</div>

<script>
(function(){
  const canvas = document.getElementById('minimap-canvas');
  const ctx = canvas.getContext('2d');
  const W=760,H=400;
  const worldW=3000,worldH=2000;
  let px=worldW/2,py=worldH/2;
  const keys={},speed=4;

  // Generate world content
  const items=[];
  for(let i=0;i<80;i++){
    const type=['tree','rock','bush'][Math.random()*3|0];
    items.push({x:Math.random()*worldW,y:Math.random()*worldH,type,s:8+Math.random()*16});
  }
  // Lakes
  const lakes=[];
  for(let i=0;i<5;i++)lakes.push({x:Math.random()*worldW,y:Math.random()*worldH,r:40+Math.random()*60});

  canvas.addEventListener('keydown',e=>{keys[e.key]=true;e.preventDefault();});
  canvas.addEventListener('keyup',e=>{keys[e.key]=false;});
  canvas.addEventListener('click',()=>canvas.focus());

  function draw(){
    if(keys['ArrowLeft']||keys['a'])px-=speed;
    if(keys['ArrowRight']||keys['d'])px+=speed;
    if(keys['ArrowUp']||keys['w'])py-=speed;
    if(keys['ArrowDown']||keys['s'])py+=speed;
    px=Math.max(20,Math.min(worldW-20,px));
    py=Math.max(20,Math.min(worldH-20,py));

    const camX=px-W/2,camY=py-H/2;

    // Draw world
    ctx.fillStyle='#1a2a1e';ctx.fillRect(0,0,W,H);

    // Grid
    ctx.strokeStyle='#1e3020';ctx.lineWidth=1;
    const gs=60;
    for(let x=-(camX%gs);x<W;x+=gs){ctx.beginPath();ctx.moveTo(x,0);ctx.lineTo(x,H);ctx.stroke();}
    for(let y=-(camY%gs);y<H;y+=gs){ctx.beginPath();ctx.moveTo(0,y);ctx.lineTo(W,y);ctx.stroke();}

    // Lakes
    lakes.forEach(l=>{
      const sx=l.x-camX,sy=l.y-camY;
      if(sx>-l.r*2&&sx<W+l.r*2&&sy>-l.r*2&&sy<H+l.r*2){
        ctx.fillStyle='#1a3a5e';ctx.beginPath();ctx.ellipse(sx,sy,l.r*1.3,l.r,0,0,Math.PI*2);ctx.fill();
        ctx.fillStyle='#2a4a6e';ctx.beginPath();ctx.ellipse(sx,sy,l.r*1.1,l.r*0.8,0,0,Math.PI*2);ctx.fill();
      }
    });

    // Items
    items.forEach(item=>{
      const sx=item.x-camX,sy=item.y-camY;
      if(sx<-30||sx>W+30||sy<-30||sy>H+30)return;
      if(item.type==='tree'){
        ctx.fillStyle='#3a2a1a';ctx.fillRect(sx-3,sy-5,6,12);
        ctx.fillStyle='#2a5a2a';ctx.beginPath();ctx.arc(sx,sy-item.s,item.s,0,Math.PI*2);ctx.fill();
      }else if(item.type==='rock'){
        ctx.fillStyle='#4a4a5a';ctx.beginPath();ctx.arc(sx,sy,item.s*0.6,0,Math.PI*2);ctx.fill();
      }else{
        ctx.fillStyle='#2a4a1a';ctx.beginPath();ctx.arc(sx,sy,item.s*0.5,0,Math.PI*2);ctx.fill();
      }
    });

    // Player
    ctx.fillStyle='#ff6a35';ctx.fillRect(W/2-8,H/2-8,16,16);
    ctx.fillStyle='#ffa07a';ctx.fillRect(W/2-5,H/2-5,5,5);

    // Minimap
    const mmW=140,mmH=mmW*(worldH/worldW),mmX=W-mmW-10,mmY=10;
    ctx.fillStyle='rgba(10,10,20,0.8)';ctx.fillRect(mmX-2,mmY-2,mmW+4,mmH+4);
    ctx.fillStyle='#1a2a1e';ctx.fillRect(mmX,mmY,mmW,mmH);

    // Lakes on minimap
    lakes.forEach(l=>{
      ctx.fillStyle='#1a3a5e';
      ctx.beginPath();ctx.arc(mmX+l.x/worldW*mmW,mmY+l.y/worldH*mmH,l.r/worldW*mmW,0,Math.PI*2);ctx.fill();
    });

    // Items on minimap
    items.forEach(item=>{
      ctx.fillStyle=item.type==='tree'?'#2a5a2a':item.type==='rock'?'#4a4a5a':'#2a4a1a';
      ctx.fillRect(mmX+item.x/worldW*mmW-1,mmY+item.y/worldH*mmH-1,2,2);
    });

    // Viewport rectangle
    ctx.strokeStyle='#ff6a35';ctx.lineWidth=1;
    const vpX=mmX+camX/worldW*mmW,vpY=mmY+camY/worldH*mmH;
    const vpW=W/worldW*mmW,vpH=H/worldH*mmH;
    ctx.strokeRect(vpX,vpY,vpW,vpH);

    // Player dot on minimap
    ctx.fillStyle='#ff6a35';
    ctx.beginPath();ctx.arc(mmX+px/worldW*mmW,mmY+py/worldH*mmH,2,0,Math.PI*2);ctx.fill();

    ctx.fillStyle='#888';ctx.font='10px monospace';
    ctx.fillText('MINIMAP',mmX,mmY+mmH+12);

    requestAnimationFrame(draw);
  }
  draw();
  canvas.focus();
})();
</script>

---

# Animation

---

## Animation State Machine

Sprite-like animation states: Idle (breathing), Walk (bouncing), Attack (swing). Shows frame timing and current state.

<div id="animsm-demo" style="background:#1a1a2e;border-radius:8px;padding:1rem;margin:1rem 0">
<canvas id="animsm-canvas" width="760" height="350" style="width:100%;border-radius:4px;outline:none" tabindex="0"></canvas>
<div style="display:flex;gap:8px;margin-top:8px;flex-wrap:wrap">
<button class="anim-btn" data-state="idle" style="padding:4px 16px;background:#ff6a35;color:#fff;border:none;border-radius:4px;cursor:pointer;font-family:monospace">Idle</button>
<button class="anim-btn" data-state="walk" style="padding:4px 16px;background:#2a2a3e;color:#eee;border:1px solid #444;border-radius:4px;cursor:pointer;font-family:monospace">Walk</button>
<button class="anim-btn" data-state="attack" style="padding:4px 16px;background:#2a2a3e;color:#eee;border:1px solid #444;border-radius:4px;cursor:pointer;font-family:monospace">Attack</button>
</div>
<p style="color:#888;font-size:12px;margin:8px 0 0">Click buttons or press 1/2/3 to switch animation state</p>
</div>

<script>
(function(){
  const canvas = document.getElementById('animsm-canvas');
  const ctx = canvas.getContext('2d');
  const W=760,H=350;
  let animState='idle',frameTimer=0,animFrame=0;

  const anims={
    idle:{frames:4,speed:12,loop:true},
    walk:{frames:6,speed:6,loop:true},
    attack:{frames:5,speed:4,loop:false}
  };

  document.querySelectorAll('.anim-btn').forEach(btn=>{
    btn.onclick=()=>{
      animState=btn.dataset.state;animFrame=0;frameTimer=0;
      document.querySelectorAll('.anim-btn').forEach(b=>{b.style.background='#2a2a3e';b.style.border='1px solid #444';});
      btn.style.background='#ff6a35';btn.style.border='none';
    };
  });

  canvas.addEventListener('keydown',e=>{
    if(e.key==='1'){animState='idle';animFrame=0;frameTimer=0;}
    if(e.key==='2'){animState='walk';animFrame=0;frameTimer=0;}
    if(e.key==='3'){animState='attack';animFrame=0;frameTimer=0;}
  });
  canvas.addEventListener('click',()=>canvas.focus());

  function drawCharacter(x,y,state,frame){
    const t=frame;
    ctx.save();ctx.translate(x,y);

    if(state==='idle'){
      // Breathing effect
      const breathe=Math.sin(t*Math.PI/2)*3;
      // Body
      ctx.fillStyle='#ff6a35';ctx.fillRect(-15,-40+breathe,30,30);
      // Head
      ctx.fillStyle='#ffa07a';ctx.beginPath();ctx.arc(0,-48+breathe,12,0,Math.PI*2);ctx.fill();
      // Eyes
      ctx.fillStyle='#1a1a2e';ctx.fillRect(-5,-51+breathe,3,3);ctx.fillRect(3,-51+breathe,3,3);
      // Legs
      ctx.fillStyle='#cc5428';ctx.fillRect(-12,-10,10,14);ctx.fillRect(2,-10,10,14);
    }else if(state==='walk'){
      // Bouncing walk
      const bounce=Math.abs(Math.sin(t*Math.PI/3))*8;
      const lean=Math.sin(t*Math.PI/3)*3;
      ctx.translate(lean,-bounce);
      // Body
      ctx.fillStyle='#ff6a35';ctx.fillRect(-15,-40,30,30);
      // Head
      ctx.fillStyle='#ffa07a';ctx.beginPath();ctx.arc(0,-48,12,0,Math.PI*2);ctx.fill();
      ctx.fillStyle='#1a1a2e';ctx.fillRect(-5,-51,3,3);ctx.fillRect(3,-51,3,3);
      // Legs (alternating)
      const legOffset=Math.sin(t*Math.PI/3)*10;
      ctx.fillStyle='#cc5428';
      ctx.fillRect(-10,-10,8,14);ctx.fillRect(2+legOffset/3,-10,8,14);
      // Arms swing
      ctx.strokeStyle='#cc5428';ctx.lineWidth=4;
      ctx.beginPath();ctx.moveTo(-15,-35);ctx.lineTo(-15-legOffset/2,-25);ctx.stroke();
      ctx.beginPath();ctx.moveTo(15,-35);ctx.lineTo(15+legOffset/2,-25);ctx.stroke();
    }else if(state==='attack'){
      // Swing animation
      const swingAngle=t<3?t*Math.PI/3:Math.PI-(t-3)*Math.PI/4;
      // Body lunge
      const lunge=t<3?t*3:Math.max(0,9-(t-3)*4);
      ctx.translate(lunge,0);
      ctx.fillStyle='#ff6a35';ctx.fillRect(-15,-40,30,30);
      ctx.fillStyle='#ffa07a';ctx.beginPath();ctx.arc(0,-48,12,0,Math.PI*2);ctx.fill();
      ctx.fillStyle='#1a1a2e';ctx.fillRect(-3,-51,3,3);ctx.fillRect(5,-51,3,3);
      ctx.fillStyle='#cc5428';ctx.fillRect(-12,-10,10,14);ctx.fillRect(2,-10,10,14);
      // Sword swing
      ctx.save();ctx.translate(15,-35);ctx.rotate(-swingAngle);
      ctx.fillStyle='#aaa';ctx.fillRect(-2,0,4,35);
      ctx.fillStyle='#ddd';ctx.fillRect(-4,-3,8,6);
      ctx.restore();
    }
    ctx.restore();
  }

  function draw(){
    frameTimer++;
    const anim=anims[animState];
    if(frameTimer>=anim.speed){
      frameTimer=0;animFrame++;
      if(animFrame>=anim.frames){
        if(anim.loop)animFrame=0;
        else{animFrame=anim.frames-1;animState='idle';animFrame=0;}
      }
    }

    ctx.fillStyle='#1a1a2e';ctx.fillRect(0,0,W,H);

    // Ground
    ctx.fillStyle='#2a2a3e';ctx.fillRect(0,H-60,W,60);

    // Character
    drawCharacter(W/2,H-60,animState,animFrame);

    // Frame display
    const frameBoxW=50,frameBoxH=50,startX=50,startY=30;
    ctx.fillStyle='#888';ctx.font='12px monospace';
    ctx.fillText(`State: ${animState}  |  Frame: ${animFrame+1}/${anims[animState].frames}  |  Speed: ${anims[animState].speed} ticks/frame`,startX,startY-8);

    for(let i=0;i<anims[animState].frames;i++){
      const fx=startX+i*(frameBoxW+8);
      ctx.fillStyle=i===animFrame?'#ff6a3533':'#2a2a3e';
      ctx.fillRect(fx,startY,frameBoxW,frameBoxH);
      ctx.strokeStyle=i===animFrame?'#ff6a35':'#444';
      ctx.lineWidth=i===animFrame?2:1;
      ctx.strokeRect(fx,startY,frameBoxW,frameBoxH);

      // Mini character in frame
      ctx.save();
      ctx.translate(fx+frameBoxW/2,startY+frameBoxH-5);
      ctx.scale(0.4,0.4);
      drawCharacter(0,0,animState,i);
      ctx.restore();

      ctx.fillStyle='#888';ctx.font='10px monospace';ctx.textAlign='center';
      ctx.fillText(`${i+1}`,fx+frameBoxW/2,startY+frameBoxH+12);
    }
    ctx.textAlign='left';

    // Timeline
    const tlY=startY+frameBoxH+25;
    ctx.fillStyle='#333';ctx.fillRect(startX,tlY,anims[animState].frames*(frameBoxW+8)-8,4);
    ctx.fillStyle='#ff6a35';
    ctx.fillRect(startX+animFrame*(frameBoxW+8),tlY-2,frameBoxW,8);

    requestAnimationFrame(draw);
  }
  draw();
  canvas.focus();
})();
</script>

---

# Input

---

## Input Visualization

Press any key or click/move the mouse to see inputs highlighted in real-time.

<div id="input-demo" style="background:#1a1a2e;border-radius:8px;padding:1rem;margin:1rem 0">
<canvas id="input-canvas" width="760" height="350" style="width:100%;border-radius:4px;outline:none;cursor:crosshair" tabindex="0"></canvas>
<p style="color:#888;font-size:12px;margin:8px 0 0">Click canvas first · Press any key to see it light up · Move mouse for position tracking</p>
</div>

<script>
(function(){
  const canvas = document.getElementById('input-canvas');
  const ctx = canvas.getContext('2d');
  const W=760,H=350;
  const activeKeys={};
  let mx=0,my=0,mouseDown=false;
  const keyHistory=[];

  // Keyboard layout (simplified)
  const rows=[
    ['Esc','','F1','F2','F3','F4','','F5','F6','F7','F8','','F9','F10','F11','F12'],
    ['`','1','2','3','4','5','6','7','8','9','0','-','=','Bksp'],
    ['Tab','Q','W','E','R','T','Y','U','I','O','P','[',']','\\'],
    ['Caps','A','S','D','F','G','H','J','K','L',';','\'','Enter'],
    ['Shift','Z','X','C','V','B','N','M',',','.','/','Shift'],
    ['Ctrl','Alt','','Space','','','Alt','Ctrl']
  ];

  const keyMap={
    'Escape':'Esc','Backspace':'Bksp','CapsLock':'Caps','ShiftLeft':'Shift','ShiftRight':'Shift',
    'ControlLeft':'Ctrl','ControlRight':'Ctrl','AltLeft':'Alt','AltRight':'Alt',
    'BracketLeft':'[','BracketRight':']','Semicolon':';','Quote':'\'','Backquote':'`',
    'Backslash':'\\','Comma':',','Period':'.','Slash':'/','Minus':'-','Equal':'=',
    'Enter':'Enter','Tab':'Tab',' ':'Space'
  };

  function getKeyLabel(code,key){
    if(keyMap[code])return keyMap[code];
    if(keyMap[key])return keyMap[key];
    if(code.startsWith('Key'))return code[3];
    if(code.startsWith('Digit'))return code[5];
    if(code.startsWith('F')&&code.length<=3)return code;
    if(key.length===1)return key.toUpperCase();
    return code;
  }

  canvas.addEventListener('keydown',e=>{
    e.preventDefault();
    const label=getKeyLabel(e.code,e.key);
    activeKeys[label]=Date.now();
    keyHistory.unshift({key:label,time:Date.now()});
    if(keyHistory.length>15)keyHistory.pop();
  });
  canvas.addEventListener('keyup',e=>{
    const label=getKeyLabel(e.code,e.key);
    delete activeKeys[label];
  });
  canvas.addEventListener('mousemove',e=>{
    const r=canvas.getBoundingClientRect();
    mx=(e.clientX-r.left)*(W/r.width);my=(e.clientY-r.top)*(H/r.height);
  });
  canvas.addEventListener('mousedown',()=>{mouseDown=true;});
  canvas.addEventListener('mouseup',()=>{mouseDown=false;});
  canvas.addEventListener('click',()=>canvas.focus());

  function draw(){
    ctx.fillStyle='#1a1a2e';ctx.fillRect(0,0,W,H);
    const now=Date.now();

    // Draw keyboard
    const startX=30,startY=20,keyW=38,keyH=28,gap=3;
    rows.forEach((row,ri)=>{
      let x=startX+ri*8; // stagger
      row.forEach(key=>{
        if(key===''){x+=keyW/2;return;}
        let w=keyW;
        if(key==='Space')w=keyW*6;
        else if(key==='Bksp'||key==='Tab'||key==='Enter'||key==='Shift'||key==='Caps')w=keyW*1.5;

        const active=activeKeys[key]!==undefined;
        const elapsed=active?0:(now-(activeKeys[key+'_last']||0));
        const fade=Math.max(0,1-elapsed/500);

        ctx.fillStyle=active?'#ff6a35':fade>0?`rgba(255,106,53,${fade*0.3})`:'#2a2a3e';
        ctx.fillRect(x,startY+ri*(keyH+gap),w-gap,keyH);
        ctx.strokeStyle=active?'#ffa07a':'#3a3a5e';ctx.lineWidth=1;
        ctx.strokeRect(x,startY+ri*(keyH+gap),w-gap,keyH);

        ctx.fillStyle=active?'#fff':'#888';ctx.font=`${key.length>3?8:10}px monospace`;ctx.textAlign='center';
        ctx.fillText(key,x+(w-gap)/2,startY+ri*(keyH+gap)+keyH/2+3);

        x+=w;
      });
    });
    ctx.textAlign='left';

    // Mouse indicator
    const mouseBoxX=560,mouseBoxY=30;
    ctx.strokeStyle='#444';ctx.lineWidth=2;
    // Mouse body
    ctx.beginPath();
    ctx.roundRect(mouseBoxX,mouseBoxY,70,100,15);
    ctx.stroke();
    // Left button
    ctx.fillStyle=mouseDown?'#ff6a35':'#2a2a3e';
    ctx.fillRect(mouseBoxX+5,mouseBoxY+5,30,35);
    // Right button
    ctx.fillStyle='#2a2a3e';ctx.fillRect(mouseBoxX+37,mouseBoxY+5,28,35);
    // Divider
    ctx.strokeStyle='#444';ctx.beginPath();ctx.moveTo(mouseBoxX+35,mouseBoxY+5);ctx.lineTo(mouseBoxX+35,mouseBoxY+45);ctx.stroke();

    // Mouse position
    ctx.fillStyle='#888';ctx.font='11px monospace';
    ctx.fillText(`Mouse: (${Math.round(mx)}, ${Math.round(my)})`,mouseBoxX-10,mouseBoxY+120);
    ctx.fillText(mouseDown?'CLICKED':'',mouseBoxX+10,mouseBoxY+135);

    // Crosshair at mouse position
    ctx.strokeStyle='#ff6a3566';ctx.lineWidth=1;
    ctx.beginPath();ctx.moveTo(mx-10,my);ctx.lineTo(mx+10,my);ctx.stroke();
    ctx.beginPath();ctx.moveTo(mx,my-10);ctx.lineTo(mx,my+10);ctx.stroke();

    // Key history
    ctx.fillStyle='#888';ctx.font='11px monospace';
    ctx.fillText('Recent keys:',30,H-10-keyHistory.length*16);
    keyHistory.forEach((kh,i)=>{
      const age=(now-kh.time)/1000;
      ctx.fillStyle=`rgba(255,106,53,${Math.max(0.2,1-age/3)})`;
      ctx.fillText(`${kh.key} (${age.toFixed(1)}s ago)`,130,H-10-((keyHistory.length-1-i)*16));
    });

    // Clean up faded keys
    Object.keys(activeKeys).forEach(k=>{
      if(!activeKeys[k])delete activeKeys[k];
    });

    requestAnimationFrame(draw);
  }
  draw();
  canvas.focus();
})();
</script>

---

All demos run entirely in-browser with vanilla JavaScript — no dependencies. The C# implementations in the [code examples](index.md) follow the same algorithms and patterns.

---

## Found this useful?

This toolkit is free — 93 docs, 63 guides, 30+ interactive demos, and growing. No paywalls, no signup walls. If these demos saved you from reinventing A* pathfinding or debugging your own easing curves, consider a small tip:

[:material-heart: Support on GitHub Sponsors](https://github.com/sponsors/sbenson2){ .md-button .md-button--primary }

Even $1 tells me people are actually using this. That's the real fuel.