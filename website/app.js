// Native controls keep the illustration lightweight; this demo never plays audio.
const playerTemplate=document.querySelector('#media-demo-template');
if(playerTemplate){
 document.querySelectorAll('[data-demo-player]').forEach(host=>{
  host.append(playerTemplate.content.cloneNode(true));
  const player=host.querySelector('.demo-player');
  const play=host.querySelector('.player-play');
  const seek=host.querySelector('.player-seek');
  const elapsed=host.querySelector('.player-elapsed');
  const titles=['Afterglow','Low tide','Blue hour'];let track=0;
  const format=value=>Math.floor(value/60)+':'+String(value%60).padStart(2,'0');
  function updatePosition(){const seconds=Number(seek.value);elapsed.textContent=format(seconds);seek.style.setProperty('--progress',seconds/Number(seek.max)*100+'%');seek.setAttribute('aria-valuetext',Math.floor(seconds/60)+' minutes '+seconds%60+' seconds');}
  play.addEventListener('click',()=>{const playing=player.dataset.playing!=='true';player.dataset.playing=String(playing);play.setAttribute('aria-pressed',String(playing));play.setAttribute('aria-label',playing?'Pause demo animation':'Play demo animation');play.querySelector('use').setAttribute('href',playing?'#icon-pause':'#icon-play');});
  seek.addEventListener('input',updatePosition);
  function changeTrack(offset){track=(track+offset+titles.length)%titles.length;host.querySelector('.demo-title').textContent=titles[track];seek.value='0';updatePosition();const island=host.closest('.demo-island');if(island){island.querySelector('.dock-glance>span:nth-child(2)').textContent=titles[track];island.querySelector('.hover-track strong').textContent=titles[track];}}
  host.querySelector('.player-next').addEventListener('click',()=>changeTrack(1));
  host.querySelector('.player-previous').addEventListener('click',()=>changeTrack(-1));
 });
}
const stage=document.querySelector('.desk');
if(stage){
 const captions={closed:'Just the essentials. Always within reach.',hover:'A quick look. Keep your flow.',open:'Your music, a little closer.'};let current='open';
 function setState(name){if(!captions[name])return;const moveFocus=stage.querySelector('.demo-island').contains(document.activeElement);current=name;stage.dataset.state=name;stage.querySelector('.dock-glance').hidden=name!=='closed';stage.querySelector('.dock-hover').hidden=name!=='hover';stage.querySelector('.dock-expanded').hidden=name!=='open';document.querySelector('#demo-caption').textContent=captions[name];document.querySelectorAll('[data-preview]').forEach(b=>b.setAttribute('aria-pressed',String(b.dataset.preview===name)));if(moveFocus)stage.querySelector('[data-preview="'+name+'"]').focus();}
 document.querySelectorAll('[data-preview]').forEach(b=>b.addEventListener('click',()=>setState(b.dataset.preview)));
 document.querySelectorAll('[data-cycle-preview]').forEach(b=>b.addEventListener('click',()=>setState(current==='closed'?'hover':current==='hover'?'open':'closed')));
 document.addEventListener('keydown',e=>{if(e.key==='Escape')setState('closed');});
 document.querySelectorAll('[data-palette]').forEach(b=>b.addEventListener('click',()=>{stage.dataset.mood=b.dataset.palette;document.querySelectorAll('[data-palette]').forEach(x=>x.setAttribute('aria-pressed',String(x===b)));}));
 const visibility=new IntersectionObserver(entries=>{entries.forEach(entry=>entry.target.classList.toggle('out-of-view',!entry.isIntersecting));});
 document.querySelectorAll('.desk,.music-frame').forEach(el=>visibility.observe(el));
 document.addEventListener('visibilitychange',()=>document.body.classList.toggle('tab-idle',document.hidden));
 setState('open');
}
const search=document.querySelector('#guide-search');
if(search){const topics=[...document.querySelectorAll('.guide-topic')];const status=document.querySelector('#guide-search-status');function filter(){let count=0;const q=search.value.trim().toLowerCase();topics.forEach(t=>{const show=!q||(t.textContent+' '+t.dataset.keywords).toLowerCase().includes(q);t.hidden=!show;if(show)count++;});document.querySelectorAll('[data-topic-link]').forEach(a=>{a.hidden=!!q&&document.querySelector(a.getAttribute('href'))?.hidden;});if(status)status.textContent=q?count+' topics found':'All topics';}search.addEventListener('input',filter);search.addEventListener('keydown',e=>{if(e.key==='Escape'){search.value='';filter();}});}
