(() => {
  const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  const isDesktopQuery = window.matchMedia('(min-width: 901px)');

  const heroSculpture = document.getElementById('hero-sculpture');
  if (heroSculpture && !prefersReducedMotion && window.matchMedia('(pointer: fine)').matches) {
    const heroSection = heroSculpture.closest('.hero-chapter');
    if (heroSection) {
      heroSection.addEventListener('pointermove', (e) => {
        const rect = heroSculpture.getBoundingClientRect();
        const cx = rect.left + rect.width / 2;
        const cy = rect.top + rect.height / 2;
        const dx = Math.max(-1, Math.min(1, (e.clientX - cx) / 340));
        const dy = Math.max(-1, Math.min(1, (e.clientY - cy) / 340));
        heroSculpture.style.transform = `rotateY(${(dx * 11).toFixed(2)}deg) rotateX(${(-dy * 11).toFixed(2)}deg)`;
      });
      heroSection.addEventListener('pointerleave', () => {
        heroSculpture.style.transform = 'rotateY(0deg) rotateX(0deg)';
      });
    }
  }

  runHeroIntro();

  function runHeroIntro() {
    const root = document.documentElement;
    if (!root.classList.contains('intro-pending')) return;

    const wrap = document.getElementById('hero-intro-wrap');
    const copy = document.querySelector('.hero-copy');
    const title = document.querySelector('.hero-title');
    if (!wrap || !copy || !title || window.scrollY > 40) {
      root.classList.remove('intro-pending');
      return;
    }

    const sculpture = document.getElementById('hero-sculpture');
    const EASE = 'cubic-bezier(0.22, 1, 0.36, 1)';
    const GLIDE_EASE = 'cubic-bezier(0.16, 1, 0.3, 1)';
    const BURST = 350;
    const GLIDE = 1200;
    const LIGHT = 1300;
    const running = [];
    let restoreTitle = null;
    let started = false;
    let finished = false;
    let finishTimer = 0;
    let lightFrame = 0;
    const lightLayers = [];

    const bloom = document.createElement('span');
    const ring = document.createElement('span');
    bloom.className = 'intro-bloom';
    ring.className = 'intro-ring';
    bloom.setAttribute('aria-hidden', 'true');
    ring.setAttribute('aria-hidden', 'true');
    wrap.prepend(bloom);
    wrap.append(ring);

    const pill = document.createElement('button');
    pill.type = 'button';
    pill.className = 'power-pill';
    pill.setAttribute('aria-label', 'Power up: enter the site');
    pill.innerHTML = '<span class="power-pill-dot" aria-hidden="true"></span><span class="power-pill-label">POWER UP</span>';
    document.body.append(pill);

    // Offset from the logo's resting place to the middle of the screen, from layout alone (offset* ignores
    // transforms), so the glide always ends at exactly zero
    function measure() {
      const parent = wrap.offsetParent.getBoundingClientRect();
      const size = sculpture.offsetWidth;
      const vh = window.innerHeight;
      const scale = Math.max(0.5, Math.min(1.12, (vh - 180) / size));
      return {
        dx: document.body.getBoundingClientRect().width / 2 - (parent.left + wrap.offsetLeft + wrap.offsetWidth / 2),
        dy: vh / 2 - (parent.top + wrap.offsetTop + wrap.offsetHeight / 2),
        scale,
        pillTop: vh / 2 + (size * scale) / 2 + Math.min(44, vh * 0.05)
      };
    }

    const transformAt = (m, scale, turn) => `translate(${m.dx}px, ${m.dy}px) rotate(${turn}deg) scale(${scale})`;

    function place() {
      if (started || finished) return;
      const m = measure();
      const lift = root.classList.contains('intro-hover') ? 1.04 : 1;
      wrap.style.transform = transformAt(m, m.scale * lift, 0);
      pill.style.top = `${m.pillTop}px`;
    }

    function play(el, frames, options) {
      const anim = el.animate(frames, Object.assign({ easing: EASE, fill: 'backwards' }, options));
      running.push(anim);
      return anim;
    }

    const setHover = on => () => {
      if (started) return;
      root.classList.toggle('intro-hover', on);
      place();
    };
    const enter = setHover(true);
    const leave = setHover(false);
    [sculpture, pill].forEach(el => {
      el.addEventListener('pointerenter', enter);
      el.addEventListener('pointerleave', leave);
      el.addEventListener('click', start);
    });

    function onKey(e) {
      if (e.repeat || e.ctrlKey || e.metaKey || e.altKey) return;
      if (e.key === 'Enter' || e.key === ' ') {
        e.preventDefault();
        start();
      }
    }
    const holdScroll = e => e.preventDefault(); // iOS ignores overflow: hidden on the page for touch
    function onScroll() {
      // A browser restoring an earlier scroll position: that visitor is already inside
      if (!started && window.scrollY > 40) finish();
    }
    let lastWidth = window.innerWidth;
    function onResize() {
      const widthChanged = window.innerWidth !== lastWidth;
      lastWidth = window.innerWidth;
      if (!started) place();
      else if (widthChanged) finish(); // Rotated mid-glide: mobile toolbars change only the height
    }
    window.addEventListener('keydown', onKey);
    window.addEventListener('scroll', onScroll, { passive: true });
    window.addEventListener('resize', onResize);
    window.addEventListener('touchmove', holdScroll, { passive: false });

    place();
    void wrap.offsetWidth; // Settle in the middle before transitions switch on
    root.classList.add('intro-ready');
    if (document.fonts && document.fonts.ready) document.fonts.ready.then(place);
    window.addEventListener('load', place);

    function unlock() {
      root.classList.remove('intro-pending');
      window.removeEventListener('keydown', onKey);
      window.removeEventListener('scroll', onScroll);
      window.removeEventListener('touchmove', holdScroll);
    }

    // Leaves nothing behind: no inline styles, no extra elements, and the title's original markup
    function finish() {
      if (finished) return;
      finished = true;
      unlock();
      clearTimeout(finishTimer);
      cancelAnimationFrame(lightFrame);
      lightLayers.forEach(el => el.remove());
      running.forEach(anim => anim.cancel());
      root.classList.remove('intro-ready', 'intro-hover', 'intro-running');
      if (restoreTitle) restoreTitle();
      wrap.removeAttribute('style');
      bloom.remove();
      ring.remove();
      pill.remove();
      window.removeEventListener('resize', onResize);
      window.removeEventListener('load', place);
      sculpture.removeEventListener('pointerenter', enter);
      sculpture.removeEventListener('pointerleave', leave);
      sculpture.removeEventListener('click', start);
    }

    function start() {
      if (started || finished) return;
      started = true;

      const m = measure();
      const lift = root.classList.contains('intro-hover') ? 1.04 : 1;
      root.classList.add('intro-running');
      root.classList.remove('intro-ready', 'intro-hover');
      pill.disabled = true;

      play(pill, [
        { opacity: 1, transform: 'translateX(-50%) translateY(0)' },
        { opacity: 0, transform: 'translateX(-50%) translateY(16px)' }
      ], { duration: 400, fill: 'forwards' });
      play(ring, [
        { opacity: 1, transform: 'translate(-50%, -50%) scale(0.98)' },
        { opacity: 0, transform: 'translate(-50%, -50%) scale(1.55)' }
      ], { duration: 720 });
      play(bloom, [
        { opacity: 0.6, transform: 'translate(-50%, -50%) scale(1.4)' },
        { opacity: 1, transform: 'translate(-50%, -50%) scale(1.8)', offset: 0.3 },
        { opacity: 0, transform: 'translate(-50%, -50%) scale(2)' }
      ], { duration: 900 });

      // One full turn that starts quickly and spends the rest of the glide easing out of it
      const total = BURST + GLIDE;
      const travel = play(wrap, [
        { opacity: getComputedStyle(wrap).opacity, transform: transformAt(m, m.scale * lift, 0), easing: 'cubic-bezier(0.4, 0, 0.6, 1)' },
        { opacity: 1, transform: transformAt(m, m.scale * 1.06, 70), offset: BURST / total, easing: GLIDE_EASE },
        { opacity: 1, transform: 'translate(0px, 0px) rotate(360deg) scale(1)' }
      ], { duration: total, easing: 'linear', fill: 'both' });

      travel.onfinish = () => {
        if (finished) return;
        wrap.removeAttribute('style');
        travel.cancel();
        play(ring, [
          { opacity: 0.6, transform: 'translate(-50%, -50%) scale(1)' },
          { opacity: 0, transform: 'translate(-50%, -50%) scale(1.3)' }
        ], { duration: 800 });
        play(bloom, [
          { opacity: 0.6, transform: 'translate(-50%, -50%) scale(1.15)' },
          { opacity: 0, transform: 'translate(-50%, -50%) scale(1.6)' }
        ], { duration: 900 });
        reveal();
      };
    }

    // Opens a soft circle from the logo's centre until it has cleared the farthest corner
    function lightUp() {
      const rect = sculpture.getBoundingClientRect();
      const x = rect.left + rect.width / 2;
      const y = rect.top + rect.height / 2;
      const from = rect.width / 2 + 60;
      const to = Math.hypot(Math.max(x, window.innerWidth - x), Math.max(y, window.innerHeight - y)) + 240;

      ['intro-dark', 'intro-wave'].forEach(name => {
        const layer = document.createElement('div');
        layer.className = name;
        layer.setAttribute('aria-hidden', 'true');
        layer.style.setProperty('--light-x', `${x}px`);
        layer.style.setProperty('--light-y', `${y}px`);
        layer.style.setProperty('--light-r', `${from}px`);
        document.body.append(layer);
        lightLayers.push(layer);
      });

      const begin = performance.now();
      const step = now => {
        const t = Math.min(1, Math.max(0, (now - begin) / LIGHT));
        const eased = t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2;
        lightLayers.forEach(layer => layer.style.setProperty('--light-r', `${from + (to - from) * eased}px`));
        lightLayers[1].style.opacity = Math.min(1, t * 8) * (1 - t * 0.5);
        if (t < 1) {
          lightFrame = requestAnimationFrame(step);
        } else {
          lightLayers.forEach(layer => layer.remove());
        }
      };
      lightFrame = requestAnimationFrame(step);
    }

    function reveal() {
      if (finished) return;
      lightUp();

      const words = splitTitle();
      const chrome = title.querySelector('.hero-title-chrome');
      const STAGGER = 140;
      const TEXT_AT = 480; // When the light is crossing the headline
      words.forEach((word, i) => {
        const inChrome = chrome && chrome.contains(word);
        // Gradient text is see-through, so a text-shadow would paint over it: those words glow with a filter instead
        const glow = strength => inChrome
          ? { filter: `drop-shadow(0 0 ${strength ? 20 : 0}px rgba(255, 255, 255, ${strength}))` }
          : { textShadow: `0 0 ${strength ? 32 : 0}px rgba(255, 255, 255, ${strength})` };
        play(word, [
          Object.assign({ opacity: 0, transform: 'translateY(0.26em) scale(0.92)' }, glow(0)),
          Object.assign({ opacity: 1, transform: 'translateY(0.03em) scale(0.994)', offset: 0.5 }, glow(0.65)),
          Object.assign({ opacity: 1, transform: 'translateY(0) scale(1)' }, glow(0))
        ], { duration: 750, delay: TEXT_AT + i * STAGGER });
      });

      const rise = [{ opacity: 0, transform: 'translateY(14px)' }, { opacity: 1, transform: 'translateY(0)' }];
      const each = (selector, frames, options) => document.querySelectorAll(selector).forEach(el => play(el, frames, options));
      const after = TEXT_AT + words.length * STAGGER;
      each('.hero-eyebrow', rise, { duration: 650, delay: TEXT_AT });
      each('.hero-pitch', rise, { duration: 650, delay: after });
      each('.hero-cta-row', rise, { duration: 650, delay: after + 110 });
      each('.hero-stat-bar', rise, { duration: 650, delay: after + 220 });

      // The text is held by its animations and everything else by the dark layer, so the page can be handed back
      unlock();
      root.classList.remove('intro-running');
      finishTimer = setTimeout(finish, after + 220 + 650 + 450);
    }

    function splitTitle() {
      const originals = [];
      const words = [];
      const walker = document.createTreeWalker(title, NodeFilter.SHOW_TEXT);
      const texts = [];
      while (walker.nextNode()) {
        if (walker.currentNode.textContent.trim()) texts.push(walker.currentNode);
      }

      texts.forEach(node => {
        const fragment = document.createDocumentFragment();
        const added = [];
        node.textContent.split(/(\s+)/).forEach(part => {
          if (!part) return;
          if (/^\s+$/.test(part)) {
            const space = document.createTextNode(part);
            fragment.append(space);
            added.push(space);
            return;
          }
          const word = document.createElement('span');
          word.className = 'hero-word';
          word.textContent = part;
          fragment.append(word);
          added.push(word);
          words.push(word);
        });
        originals.push({ node, added });
        node.replaceWith(fragment);
      });

      // Each chrome word shows its own slice of the line's gradient, so restoring the plain text changes nothing visible
      const chrome = title.querySelector('.hero-title-chrome');
      if (chrome) {
        const box = chrome.getBoundingClientRect();
        chrome.querySelectorAll('.hero-word').forEach(word => {
          const rect = word.getBoundingClientRect();
          word.style.backgroundSize = `100% ${box.height}px`;
          word.style.backgroundPosition = `0 ${box.top - rect.top}px`;
        });
      }

      restoreTitle = () => {
        originals.forEach(({ node, added }) => {
          added[0].before(node);
          added.forEach(el => el.remove());
        });
      };
      return words;
    }
  }

  const manifestoLines = document.querySelectorAll('[data-manifesto-line]');
  const manifestoWords = [];
  manifestoLines.forEach(line => {
    const words = line.textContent.trim().split(/\s+/);
    line.textContent = '';
    words.forEach((word, i) => {
      const span = document.createElement('span');
      span.className = 'm-word';
      span.textContent = word;
      line.appendChild(span);
      manifestoWords.push(span);
      if (i < words.length - 1) {
        line.appendChild(document.createTextNode(' '));
      }
    });
  });

  const manifestoTrack = document.getElementById('manifesto');

  const dialTicks = document.getElementById('giant-dial-ticks');
  if (dialTicks) {
    const svgNS = 'http://www.w3.org/2000/svg';
    const count = 120;
    const cx = 600;
    const cy = 600;
    const rOut = 575;
    for (let i = 0; i < count; i++) {
      const deg = (i * 360) / count;
      const rad = (deg * Math.PI) / 180;
      const isMajor = i % 12 === 0;
      const isMid = i % 6 === 0 && !isMajor;
      const rIn = isMajor ? 536 : isMid ? 550 : 562;
      const line = document.createElementNS(svgNS, 'line');
      line.setAttribute('x1', (cx + Math.cos(rad) * rIn).toFixed(2));
      line.setAttribute('y1', (cy + Math.sin(rad) * rIn).toFixed(2));
      line.setAttribute('x2', (cx + Math.cos(rad) * rOut).toFixed(2));
      line.setAttribute('y2', (cy + Math.sin(rad) * rOut).toFixed(2));
      line.setAttribute('stroke', isMajor ? 'rgba(255,255,255,0.68)' : isMid ? 'rgba(255,255,255,0.34)' : 'rgba(255,255,255,0.18)');
      line.setAttribute('stroke-width', isMajor ? '2' : '1');
      dialTicks.appendChild(line);
    }
  }

  const CHAPTERS = [
    {
      angle: 0,
      hotkey: 'Alt + L · Click-Through',
      caption: '<strong>In-Game Overlay:</strong> Forza Horizon with lyrics on top. Lock the overlay with <code>Alt + L</code> and clicks go to the game.'
    },
    {
      angle: -36,
      hotkey: 'Album Art · 36–180px',
      caption: '<strong>Desktop Widget:</strong> Quick-access bars, Midnight Bloom widget mode and album art you can size from 36px to 180px.'
    },
    {
      angle: -72,
      hotkey: 'Alt + F · 60 FPS Kinetic',
      caption: '<strong>Fullscreen Vibe:</strong> Moonlight Bloom, Fisheye, a window that snaps, and Ambient Idle when you pause.'
    }
  ];

  const showcaseTrack = document.getElementById('showcase');
  const videoLayers = Array.from(document.querySelectorAll('.stage-video-layer'));
  const chapterBtns = Array.from(document.querySelectorAll('.chapter-pill-btn'));
  const showcaseCaption = document.getElementById('showcase-caption');
  const showcaseHotkey = document.getElementById('showcase-hotkey');
  const muteBtn = document.getElementById('cinema-mute-btn');
  const muteIcon = document.getElementById('cinema-mute-icon');
  const muteLabel = document.getElementById('cinema-mute-label');
  const progressFill = document.getElementById('cinema-progress-fill');
  const cinemaTime = document.getElementById('cinema-time');
  const cinemaShell = document.getElementById('cinema-video-shell');

  let activeChapter = 0;
  let showcaseInView = false;
  let isMuted = true;

  function formatTime(sec) {
    if (!Number.isFinite(sec) || sec < 0) return '00:00';
    const m = Math.floor(sec / 60);
    const s = Math.floor(sec % 60);
    return `${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`;
  }

  function syncMuteUI() {
    if (!muteBtn || !muteIcon || !muteLabel) return;
    muteBtn.setAttribute('aria-label', isMuted ? 'Unmute active video' : 'Mute active video');
    muteLabel.textContent = isMuted ? 'Muted' : 'Sound On';
    muteIcon.innerHTML = isMuted
      ? '<polygon points="11 5 6 9 2 9 2 15 6 15 11 19 11 5"/><line x1="23" y1="9" x2="17" y2="15"/><line x1="17" y1="9" x2="23" y2="15"/>'
      : '<polygon points="11 5 6 9 2 9 2 15 6 15 11 19 11 5"/><path d="M15.54 8.46a5 5 0 0 1 0 7.07"/><path d="M19.07 4.93a10 10 0 0 1 0 14.14"/>';
  }

  function updateActiveVideoProgress() {
    const activeVid = videoLayers[activeChapter];
    if (!activeVid || !progressFill || !cinemaTime) return;
    const cur = activeVid.currentTime || 0;
    const dur = activeVid.duration || 0;
    cinemaTime.textContent = `${formatTime(cur)} / ${formatTime(dur)}`;
    progressFill.style.width = dur > 0 ? `${((cur / dur) * 100).toFixed(2)}%` : '0%';
  }

  function syncVideoPlayback() {
    videoLayers.forEach((vid, idx) => {
      vid.muted = isMuted;
      if (idx === activeChapter && showcaseInView && !prefersReducedMotion) {
        playVideo(vid);
      } else {
        // Also cancels a pending autoplay, so hidden videos neither run nor download
        vid.pause();
      }
    });
  }

  function playVideo(vid) {
    const attempt = vid.play();
    if (!attempt) return;
    attempt.catch(err => {
      // Phones refuse sound without a fresh tap: fall back to muted rather than freeze on a frame
      if (err && err.name === 'NotAllowedError' && !vid.muted && vid === videoLayers[activeChapter]) {
        isMuted = true;
        vid.muted = true;
        syncMuteUI();
        vid.play().catch(() => {});
      }
    });
  }

  document.addEventListener('visibilitychange', () => {
    if (!document.hidden) syncVideoPlayback();
  });

  function setChapter(idx, updateDialAngleDirectly = true) {
    const clamped = Math.max(0, Math.min(CHAPTERS.length - 1, idx));
    const ch = CHAPTERS[clamped];

    if (updateDialAngleDirectly) {
      document.documentElement.style.setProperty('--dial-angle', `${ch.angle}deg`);
    }

    if (clamped === activeChapter && document.body.getAttribute('data-channel') === String(clamped)) {
      return;
    }

    activeChapter = clamped;
    document.body.setAttribute('data-channel', String(clamped));

    chapterBtns.forEach((btn, i) => {
      btn.setAttribute('aria-selected', i === clamped ? 'true' : 'false');
    });

    videoLayers.forEach((vid, i) => {
      vid.classList.toggle('is-active', i === clamped);
    });

    if (showcaseCaption) showcaseCaption.innerHTML = ch.caption;
    if (showcaseHotkey) showcaseHotkey.textContent = ch.hotkey;

    syncVideoPlayback();
    updateActiveVideoProgress();
  }

  videoLayers.forEach((vid, idx) => {
    vid.addEventListener('timeupdate', () => {
      if (idx === activeChapter) updateActiveVideoProgress();
    });
    vid.addEventListener('loadedmetadata', () => {
      if (idx === activeChapter) updateActiveVideoProgress();
    });
  });

  if (muteBtn) {
    muteBtn.addEventListener('click', () => {
      isMuted = !isMuted;
      syncMuteUI();
      const activeVid = videoLayers[activeChapter];
      if (activeVid) {
        activeVid.muted = isMuted;
        if (!isMuted && activeVid.paused) {
          activeVid.play().catch(() => {});
        }
      }
    });
  }

  const showcaseSteps = Array.from(document.querySelectorAll('.showcase-step'));
  let pendingChapter = -1;
  let pendingTimer = 0;

  // Scroll to the target chapter's third, so the page position and the selected video always agree
  function goToChapter(idx) {
    if (showcaseTrack) {
      const trackTop = window.scrollY + showcaseTrack.getBoundingClientRect().top;
      const maxScrollSpan = Math.max(1, showcaseTrack.offsetHeight - window.innerHeight);
      const sliceTargets = [0.10, 0.48, 0.86];
      const targetY = trackTop + maxScrollSpan * sliceTargets[idx];
      window.scrollTo({ top: targetY, behavior: prefersReducedMotion ? 'auto' : 'smooth' });
    }
    if (!isDesktopQuery.matches) {
      // Pass over the chapters in between without flashing them
      pendingChapter = idx;
      clearTimeout(pendingTimer);
      pendingTimer = setTimeout(() => { pendingChapter = -1; }, 1200);
    }
    setChapter(idx, false);
  }

  chapterBtns.forEach((btn, idx) => {
    btn.addEventListener('click', () => goToChapter(idx));
  });

  if ('IntersectionObserver' in window) {
    // A thin band across the middle of the screen: the step inside it is the current chapter
    const stepsInBand = new Set();
    const stepObs = new IntersectionObserver((entries) => {
      entries.forEach(entry => {
        const idx = Number(entry.target.dataset.step);
        if (entry.isIntersecting) stepsInBand.add(idx);
        else stepsInBand.delete(idx);
      });
      // Two at once means a boundary is crossing: keep the current chapter until one of them has left
      if (isDesktopQuery.matches || stepsInBand.size !== 1) return;

      const [idx] = stepsInBand;
      if (pendingChapter >= 0) {
        if (idx !== pendingChapter) return;
        pendingChapter = -1;
      }
      setChapter(idx, false);
    }, { rootMargin: '-48% 0px -48% 0px', threshold: 0 });
    showcaseSteps.forEach(step => stepObs.observe(step));
  }

  if (cinemaShell) {
    let startX = 0;
    let startY = 0;
    let pointerDown = false;
    cinemaShell.addEventListener('pointerdown', (e) => {
      if (e.target.closest('.cinema-hud')) return;
      pointerDown = true;
      startX = e.clientX;
      startY = e.clientY;
    });
    cinemaShell.addEventListener('pointerup', (e) => {
      if (!pointerDown) return;
      pointerDown = false;
      const dx = e.clientX - startX;
      const dy = e.clientY - startY;
      if (Math.abs(dx) > 48 && Math.abs(dx) > Math.abs(dy) * 1.3) {
        const next = (activeChapter + (dx < 0 ? 1 : -1) + CHAPTERS.length) % CHAPTERS.length;
        if (isDesktopQuery.matches) setChapter(next, true);
        else goToChapter(next);
      } else if (Math.abs(dx) < 8 && Math.abs(dy) < 8) {
        const activeVid = videoLayers[activeChapter];
        if (activeVid) {
          if (activeVid.paused) playVideo(activeVid);
          else activeVid.pause();
        }
      }
    });
    cinemaShell.addEventListener('pointercancel', () => { pointerDown = false; });
  }

  if (cinemaShell && 'IntersectionObserver' in window) {
    const vidObs = new IntersectionObserver((entries) => {
      entries.forEach(entry => {
        showcaseInView = entry.isIntersecting;
        syncVideoPlayback();
      });
    }, { threshold: 0.25 });
    vidObs.observe(cinemaShell);
  }

  let scrollTicking = false;

  function onScrollFrame() {
    const vh = window.innerHeight || 800;

    if (manifestoTrack && manifestoWords.length > 0 && isDesktopQuery.matches && !prefersReducedMotion) {
      const rect = manifestoTrack.getBoundingClientRect();
      const span = Math.max(1, manifestoTrack.offsetHeight - vh);
      const progress = Math.min(1, Math.max(0, -rect.top / span));
      const litCount = Math.floor(progress * (manifestoWords.length + 2));
      for (let i = 0; i < manifestoWords.length; i++) {
        manifestoWords[i].classList.toggle('is-lit', i < litCount);
      }
    } else if (manifestoWords.length > 0) {
      manifestoWords.forEach(w => w.classList.add('is-lit'));
    }

    if (showcaseTrack && isDesktopQuery.matches) {
      const rect = showcaseTrack.getBoundingClientRect();

      if (!prefersReducedMotion) {
        const enterRatio = Math.min(1, Math.max(0, (vh - rect.top) / (vh * 0.85)));
        const scale = 0.84 + 0.16 * enterRatio;
        document.documentElement.style.setProperty('--stage-scale', scale.toFixed(4));
      } else {
        document.documentElement.style.setProperty('--stage-scale', '1');
      }

      const trackSpan = Math.max(1, showcaseTrack.offsetHeight - vh);
      const trackProgress = Math.min(1, Math.max(0, -rect.top / trackSpan));

      const targetChapter = trackProgress < 0.333 ? 0 : trackProgress < 0.666 ? 1 : 2;
      if (prefersReducedMotion) {
        document.documentElement.style.setProperty('--dial-angle', `${CHAPTERS[targetChapter].angle}deg`);
      } else {
        const angle = -72 * trackProgress;
        document.documentElement.style.setProperty('--dial-angle', `${angle.toFixed(2)}deg`);
      }

      if (targetChapter !== activeChapter) {
        setChapter(targetChapter, false);
      }
    } else if (showcaseTrack) {
      // Touch layouts: the same scroll-linked spin as desktop, while the step observer picks the chapter
      document.documentElement.style.setProperty('--stage-scale', '1');
      const trackTop = showcaseTrack.getBoundingClientRect().top + window.scrollY;
      const trackSpan = Math.max(1, showcaseTrack.offsetHeight - vh);
      const trackProgress = Math.min(1, Math.max(0, (window.scrollY - trackTop) / trackSpan));
      const angle = prefersReducedMotion ? CHAPTERS[activeChapter].angle : -72 * trackProgress;
      document.documentElement.style.setProperty('--dial-angle', `${angle.toFixed(2)}deg`);
    }

    scrollTicking = false;
  }

  window.addEventListener('scroll', () => {
    if (!scrollTicking) {
      requestAnimationFrame(onScrollFrame);
      scrollTicking = true;
    }
  }, { passive: true });
  window.addEventListener('resize', onScrollFrame);
  onScrollFrame();
  syncMuteUI();
  syncVideoPlayback();

  const THEMES = [
    { name: 'Midnight Bloom lyrics', caption: '<strong>Midnight Bloom:</strong> glowing words on a black card, revealed in time with the vocals.' },
    { name: 'Glass lyrics', caption: '<strong>Glass:</strong> a dark frosted card that keeps lyrics readable over anything.' },
    { name: 'Transparent lyrics', caption: '<strong>Transparent:</strong> no card, just lyrics and album art over your desktop or game.' },
    { name: 'Glass clock', caption: '<strong>Glass Clock:</strong> the idle clock on the same frosted card when the music stops.' },
    { name: 'Transparent clock', caption: '<strong>Transparent Clock:</strong> only the time and date, straight on the wallpaper.' }
  ];
  const themePills = Array.from(document.querySelectorAll('.theme-pill'));
  const themeShots = Array.from(document.querySelectorAll('.theme-shot'));
  const themeOpen = document.getElementById('theme-open');
  const themeCaption = document.getElementById('theme-caption');
  const lightbox = document.getElementById('theme-lightbox');
  const lightboxImg = document.getElementById('theme-lightbox-img');
  let activeTheme = 0;

  function setTheme(idx) {
    if (idx === activeTheme || !THEMES[idx]) return;
    activeTheme = idx;
    themePills.forEach((pill, i) => pill.setAttribute('aria-pressed', i === idx ? 'true' : 'false'));
    themeShots.forEach((shot, i) => {
      shot.classList.toggle('is-active', i === idx);
      if (i === idx) shot.removeAttribute('aria-hidden');
      else shot.setAttribute('aria-hidden', 'true');
    });
    themeOpen.setAttribute('aria-label', `View ${THEMES[idx].name} full size`);
    themeCaption.innerHTML = THEMES[idx].caption;
    if (!prefersReducedMotion && themeCaption.animate) {
      themeCaption.animate([{ opacity: 0, transform: 'translateY(6px)' }, { opacity: 1, transform: 'none' }], { duration: 420, easing: 'cubic-bezier(0.22, 1, 0.36, 1)' });
    }
  }

  themePills.forEach((pill, i) => pill.addEventListener('click', () => setTheme(i)));

  if (themeOpen && lightbox && lightbox.showModal) {
    themeOpen.addEventListener('click', () => {
      const shot = themeShots[activeTheme];
      lightboxImg.src = shot.src;
      lightboxImg.alt = shot.alt;
      lightbox.showModal(); // Esc and focus are handled by the dialog itself
    });
    lightbox.addEventListener('click', () => lightbox.close());
  }

  const revealEls = document.querySelectorAll('.reveal');
  if (prefersReducedMotion || !('IntersectionObserver' in window)) {
    revealEls.forEach(el => el.classList.add('is-in'));
  } else {
    const revObs = new IntersectionObserver((entries, obs) => {
      entries.forEach(entry => {
        if (entry.isIntersecting) {
          entry.target.classList.add('is-in');
          obs.unobserve(entry.target);
        }
      });
    }, { threshold: 0.14, rootMargin: '0px 0px -30px 0px' });
    revealEls.forEach(el => revObs.observe(el));
  }

  const downloadEmblem = document.getElementById('download-emblem');
  const downloadSection = document.getElementById('download');

  function triggerDownloadFlash() {
    if (!downloadEmblem || prefersReducedMotion) return;
    downloadEmblem.classList.remove('is-flashing');
    void downloadEmblem.offsetWidth;
    downloadEmblem.classList.add('is-flashing');
  }

  if (downloadSection && 'IntersectionObserver' in window) {
    const dlObs = new IntersectionObserver((entries) => {
      entries.forEach(entry => {
        if (entry.isIntersecting) triggerDownloadFlash();
      });
    }, { threshold: 0.45 });
    dlObs.observe(downloadSection);
  }

  document.querySelectorAll('[data-scroll-download]').forEach(trigger => {
    trigger.addEventListener('click', () => {
      setTimeout(triggerDownloadFlash, 520);
    });
  });

  const legalBtns = document.querySelectorAll('.legal-tab-btn');
  const privacyPanel = document.getElementById('legal-panel-privacy');
  const termsPanel = document.getElementById('legal-panel-terms');

  legalBtns.forEach(btn => {
    btn.addEventListener('click', () => {
      const target = btn.dataset.legalTab;
      legalBtns.forEach(b => b.setAttribute('aria-selected', b === btn ? 'true' : 'false'));
      if (privacyPanel && termsPanel) {
        privacyPanel.classList.toggle('is-active', target === 'privacy');
        termsPanel.classList.toggle('is-active', target === 'terms');
      }
    });
  });
})();
