(() => {
  const USS_URL = '/client/Unity/Assets/UI/HUD.uss';
  const PLAYER_COLORS = ['#fbbf24', '#ea580c', '#3b82f6', '#22c55e'];

  function translateUss(uss) {
    const replacements = [
      [/-unity-font-style\s*:\s*bold\s*;/g, 'font-weight: 700;'],
      [/-unity-text-align\s*:\s*middle-center\s*;/g, 'text-align: center; align-items: center; justify-content: center;'],
      [/-unity-text-align\s*:\s*middle-right\s*;/g, 'text-align: right; align-items: center; justify-content: flex-end;'],
      [/-unity-text-align\s*:\s*middle-left\s*;/g, 'text-align: left; align-items: center; justify-content: flex-start;'],
      [/-unity-text-outline-width\s*:\s*([^;]+);/g, '-webkit-text-stroke-width: $1; paint-order: stroke fill;'],
      [/-unity-text-outline-color\s*:\s*([^;]+);/g, '-webkit-text-stroke-color: $1;'],
      [/-unity-background-scale-mode\s*:\s*scale-to-fit\s*;/g, 'background-size: contain; background-position: center; background-repeat: no-repeat;'],
      [/-unity-background-scale-mode\s*:\s*scale-and-crop\s*;/g, 'background-size: cover; background-position: center; background-repeat: no-repeat;'],
    ];

    let css = uss;
    for (const [pattern, replacement] of replacements) css = css.replace(pattern, replacement);

    const unsupported = [...css.matchAll(/-unity-[a-z-]+\s*:/g)].map(match => match[0].slice(0, -1));
    return { css, unsupported: [...new Set(unsupported)] };
  }

  function applyRuntimeStyles() {
    document.querySelectorAll('.billboard-card').forEach((card, index) => {
      const color = PLAYER_COLORS[index % PLAYER_COLORS.length];
      card.style.borderBottomColor = color;
      const portraitFrame = card.querySelector('.portrait-frame');
      const badge = card.querySelector('.badge');
      if (portraitFrame) portraitFrame.style.backgroundColor = color;
      if (badge) badge.style.backgroundColor = color;
    });

    document.querySelectorAll('.overhead-panel').forEach(panel => {
      const index = Number(panel.dataset.player) - 1;
      const damage = panel.querySelector('.overhead-damage');
      if (damage) damage.style.color = PLAYER_COLORS[index % PLAYER_COLORS.length];
    });
  }

  window.hudUssReady = fetch(USS_URL, { cache: 'no-store' })
    .then(response => {
      if (!response.ok) throw new Error(`${response.status} ${response.statusText}`);
      return response.text();
    })
    .then(uss => {
      const translated = translateUss(uss);
      const style = document.createElement('style');
      style.id = 'translated-hud-uss';
      style.dataset.source = USS_URL;
      style.textContent = `
        .hud-root, .hud-root * { box-sizing: border-box; display: flex; flex-direction: column; flex-shrink: 1; }
        ${translated.css}
      `;
      document.head.append(style);
      applyRuntimeStyles();

      const status = document.querySelector('#uss-status');
      if (status) {
        status.classList.add('loaded');
        status.textContent = translated.unsupported.length === 0
          ? `Loaded ${USS_URL} directly · ${uss.length.toLocaleString()} bytes · all Unity declarations mapped`
          : `Loaded ${USS_URL} · unsupported: ${translated.unsupported.join(', ')}`;
      }
      return { source: USS_URL, bytes: uss.length, unsupported: translated.unsupported };
    })
    .catch(error => {
      const status = document.querySelector('#uss-status');
      if (status) { status.classList.add('failed'); status.textContent = `HUD.uss failed to load: ${error.message}`; }
      throw error;
    });
})();
