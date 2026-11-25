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
      idx: 0,
      angle: 0,
      hotkey: 'Alt + L · Click-Through',
      caption: '<strong>In-Game Overlay:</strong> Forza Horizon gameplay running with <code>Alt + L</code> click-through Gaming Mode and zero frame drops.'
    },
    {
      idx: 1,
      angle: -36,
      hotkey: 'Alt + T · Glassify HUD',
      caption: '<strong>Desktop Widget:</strong> Switch between borderless Transparent lyrics, frosted Glassify acrylic cards, and the idle clock.'
    },
    {
      idx: 2,
      angle: -72,
      hotkey: 'Alt + F · 60 FPS Kinetic',
      caption: '<strong>Fullscreen 60 FPS:</strong> LiveCode IDE syntax mode, SpotlightFog spring scroll, and 1200x1200 HD album artwork.'
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
        vid.play().catch(() => {});
      } else {
        if (!vid.paused) vid.pause();
      }
    });
  }

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

  // Scroll to the target chapter's third on desktop, or switch immediately on mobile
  chapterBtns.forEach((btn, idx) => {
    btn.addEventListener('click', () => {
      if (isDesktopQuery.matches && showcaseTrack) {
        const trackTop = window.scrollY + showcaseTrack.getBoundingClientRect().top;
        const maxScrollSpan = Math.max(1, showcaseTrack.offsetHeight - window.innerHeight);
        // Aim squarely inside that chapter's third (0.12, 0.48, 0.85)
        const sliceTargets = [0.10, 0.48, 0.86];
        const targetY = trackTop + maxScrollSpan * sliceTargets[idx];
        window.scrollTo({ top: targetY, behavior: prefersReducedMotion ? 'auto' : 'smooth' });
      }
      setChapter(idx, !isDesktopQuery.matches);
    });
  });

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
        setChapter(next, true);
      } else if (Math.abs(dx) < 8 && Math.abs(dy) < 8) {
        const activeVid = videoLayers[activeChapter];
        if (activeVid) {
          if (activeVid.paused) activeVid.play().catch(() => {});
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
        // Rotate dial continuously across the full showcase track (-72deg total)
        const angle = -72 * trackProgress;
        document.documentElement.style.setProperty('--dial-angle', `${angle.toFixed(2)}deg`);
      }

      if (targetChapter !== activeChapter) {
        setChapter(targetChapter, false);
      }
    } else {
      document.documentElement.style.setProperty('--stage-scale', '1');
      document.documentElement.style.setProperty('--dial-angle', `${CHAPTERS[activeChapter].angle}deg`);
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
