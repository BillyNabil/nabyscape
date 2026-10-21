'use strict';
(() => {
  const shell=document.querySelector('.demo-shell');
  const awake=()=>document.body.classList.toggle('site-awake',!document.hidden);awake();document.addEventListener('visibilitychange',awake);
  if(shell){
    const observer=new IntersectionObserver(entries=>entries.forEach(entry=>shell.classList.toggle('in-view',entry.isIntersecting)),{threshold:.1});observer.observe(shell);
    const choices=[...document.querySelectorAll('[data-ambience]')];choices.forEach(button=>button.addEventListener('click',()=>{shell.dataset.ambience=button.dataset.ambience;choices.forEach(choice=>choice.setAttribute('aria-pressed',String(choice===button)));}));
  }
  const stage = document.querySelector('.desktop-stage');
  if (stage) {
    const preview = document.querySelector('.island-preview');
    const image = document.querySelector('#island-image');
    const stateButtons = [...document.querySelectorAll('[data-state-button]')];
    const states = {
      closed: {src: 'closed-raw.png', width:328, height:36, alt:'Closed NabyScape island showing sample artwork, waveform and clock'},
      hover: {src:'hover-music-raw.png', width:448, height:207, alt:'Actual music hover preview with sample artwork, playback and volume controls'},
      open: {src:'media-raw.png', width:526, height:334, alt:'Actual expanded Music panel with sample track, playback, waveform, timeline and volume'}
    };
    let pinned = false;
    const setState = (name, persist = true) => {
      if (!states[name]) return;
      if (persist) pinned = name !== 'closed';
      const state = states[name];
      stage.dataset.state = name;
      image.src = '/assets/' + state.src;
      image.alt = state.alt;
      image.width = state.width;
      image.height = state.height;
      preview.setAttribute('aria-label', name === 'open' ? 'Close the island preview' : 'Open the island preview');
      stateButtons.forEach(button => button.setAttribute('aria-pressed', String(button.dataset.stateButton === name)));
      document.querySelector('#state-status').textContent = name[0].toUpperCase() + name.slice(1);
    };
    stateButtons.forEach(button => button.addEventListener('click', () => setState(button.dataset.stateButton)));
    preview.addEventListener('pointerenter', event => {if (event.pointerType !== 'touch' && !pinned) setState('hover', false);});
    preview.addEventListener('pointerleave', () => {if (!pinned) setState('closed', false);});
    preview.addEventListener('click', () => setState(stage.dataset.state === 'open' ? 'closed' : 'open'));
    document.querySelector('.demo-shell').addEventListener('keydown', event => {if (event.key === 'Escape') {setState('closed'); preview.focus();}});
    // Load only the two small next-state images; the actual film stays preload=none.
    ['hover','open'].forEach(name => {const img = new Image(); img.src = '/assets/' + states[name].src;});
  }

  const tabs = [...document.querySelectorAll('[data-feature]')];
  if (tabs.length) {
    const tabLayout = window.matchMedia('(max-width: 800px)');
    const updateOrientation = () => document.querySelector('.feature-menu').setAttribute('aria-orientation', tabLayout.matches ? 'horizontal' : 'vertical');
    updateOrientation();
    tabLayout.addEventListener('change', updateOrientation);
    const features = {
      music: ['media-raw.png','Actual Music panel with sample track, waveform, seek and volume controls','Keep the song. Skip the detour.','Play, pause, seek and switch Windows media sessions. The amplitude waveform follows the selected app, with output volume and an app mixer close by.','music','Explore music controls'],
      system: ['system.webp','Actual System overview showing readings from the capture machine','A quick look at your PC.','CPU, RAM, GPU and uptime in one overview. Open Hardware, Network, Storage or Processes for details. Sensor availability depends on your PC and Windows.','system','Understand system readings'],
      notes: ['tasks.webp','Actual Tasks panel containing original sample to-do items','A thought. A task. Back to work.','Keep one local note shared by the island and its detached view. Add small to-dos in Tasks, check them off, and return to what you were doing.','tools','Explore notes and tasks'],
      shelf: ['shelf.webp','Actual File shelf containing two sample files and selection controls','Files, ready to hand over.','Drop files or folders onto the island, select a few, and drag them out together. The shelf holds references to up to 40 items. Your originals stay in place.','tools','Read about the file shelf'],
      tools: ['tools.webp','Actual Tools menu with local utilities','The little tools you keep needing.','A timer, Focus sessions, a local calendar, opt-in clipboard history and quick launch targets. Keep the tools and hover pages you use; turn the others off.','tools','Find your everyday tools']
    };
    const selectTab = (tab, focus = false) => {
      const feature = features[tab.dataset.feature];
      tabs.forEach(item => {item.setAttribute('aria-selected', String(item === tab)); item.tabIndex = item === tab ? 0 : -1;});
      const image = document.querySelector('#feature-image'); image.src = '/assets/' + feature[0]; image.alt = feature[1]; image.removeAttribute('width'); image.removeAttribute('height');
      document.querySelector('#feature-heading').textContent = feature[2];
      document.querySelector('#feature-description').textContent = feature[3];
      const guide = document.querySelector('#feature-guide'); guide.href = '/docs/#' + feature[4]; guide.textContent = feature[5] + ' ↗';
      document.querySelector('#feature-panel').setAttribute('aria-labelledby',tab.id);
      if (focus) tab.focus();
    };
    tabs.forEach((tab,index) => {
      tab.addEventListener('click', () => selectTab(tab));
      tab.addEventListener('keydown', event => {
        let next;
        if (event.key === 'ArrowDown' || event.key === 'ArrowRight') next = (index+1)%tabs.length;
        if (event.key === 'ArrowUp' || event.key === 'ArrowLeft') next = (index-1+tabs.length)%tabs.length;
        if (event.key === 'Home') next=0;
        if (event.key === 'End') next=tabs.length-1;
        if (next !== undefined) {event.preventDefault(); selectTab(tabs[next],true);}
      });
    });
  }

  const search = document.querySelector('#guide-search');
  if (search) {
    const topics = [...document.querySelectorAll('.guide-topic')];
    const links = [...document.querySelectorAll('[data-topic-link]')];
    const filter = () => {
      const words = search.value.toLocaleLowerCase().trim().split(/\s+/).filter(Boolean);
      let count = 0;
      topics.forEach(topic => {
        const matches = words.every(word => (topic.textContent + ' ' + (topic.dataset.keywords || '')).toLocaleLowerCase().includes(word));
        topic.hidden = !matches;
        const link = links.find(item => item.getAttribute('href') === '#' + topic.id);
        if (link) link.hidden = !matches;
        if (matches) count++;
      });
      document.querySelector('#guide-search-status').textContent = words.length ? count + ' topic' + (count === 1 ? '' : 's') + ' found' : 'Search the guide or choose a topic.';
      document.querySelector('#no-results').hidden = count !== 0;
    };
    search.addEventListener('input', filter);
    search.addEventListener('keydown', event => {if(event.key === 'Escape'){search.value='';filter();}});
    const revealTopic = hash => {
      const target = document.getElementById(hash.slice(1));
      if (target && target.classList.contains('guide-topic')) {
        if (target.hidden) {search.value='';filter();}
        target.scrollIntoView();
      }
    };
    const revealHash = () => revealTopic(location.hash);
    document.addEventListener('click', event => {
      if (event.defaultPrevented || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
      const link = event.target.closest('a[href^="#"]');
      if (link) revealTopic(link.getAttribute('href'));
    });
    window.addEventListener('hashchange',revealHash);
    filter();revealHash();
  }
})();
