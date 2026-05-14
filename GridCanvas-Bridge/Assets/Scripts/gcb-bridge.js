'use strict';

const GCB = (() => {
  const COLS = 24;
  const ROWS = 24;

  let selectedEl = null;
  let dragState = null;

  // グリッド座標 → CSS % 変換
  const toPctX = (g) => (g / COLS * 100).toFixed(4) + '%';
  const toPctY = (g) => (g / ROWS * 100).toFixed(4) + '%';

  function snapToGrid(px, containerSize, cells) {
    const cellSize = containerSize / cells;
    return Math.max(0, Math.min(cells - 1, Math.round(px / cellSize)));
  }

  function applyGridPos(el, gridX, gridY, gridW, gridH) {
    el.style.left = toPctX(gridX);
    el.style.top = toPctY(gridY);
    el.style.width = toPctX(gridW);
    el.style.height = toPctY(gridH);
    el.dataset.gridX = gridX;
    el.dataset.gridY = gridY;
    el.dataset.gridW = gridW;
    el.dataset.gridH = gridH;
  }

  function postMessage(msg) {
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.postMessage(msg);
    }
  }

  function selectElement(el) {
    if (selectedEl) selectedEl.classList.remove('selected');
    selectedEl = el;
    if (el) {
      el.classList.add('selected');
      postMessage({
        type: 'elementSelected',
        id: el.id,
        gridX: +el.dataset.gridX,
        gridY: +el.dataset.gridY,
        gridW: +el.dataset.gridW,
        gridH: +el.dataset.gridH,
      });
    } else {
      postMessage({ type: 'elementDeselected' });
    }
  }

  function onMouseDown(e) {
    const el = e.currentTarget;
    if (e.target !== el) return; // contenteditable 内クリックは D&D しない

    e.preventDefault();
    selectElement(el);

    const root = document.getElementById('slide-root');
    const rect = root.getBoundingClientRect();

    dragState = {
      el,
      startMouseX: e.clientX,
      startMouseY: e.clientY,
      startGridX: +el.dataset.gridX,
      startGridY: +el.dataset.gridY,
      gridW: +el.dataset.gridW,
      gridH: +el.dataset.gridH,
      rootW: rect.width,
      rootH: rect.height,
    };
    el.classList.add('dragging');
  }

  function onMouseMove(e) {
    if (!dragState) return;
    const { el, startMouseX, startMouseY, startGridX, startGridY, gridW, gridH, rootW, rootH } = dragState;

    const dx = e.clientX - startMouseX;
    const dy = e.clientY - startMouseY;
    const newGridX = Math.max(0, Math.min(COLS - gridW, snapToGrid(startGridX * (rootW / COLS) + dx, rootW, COLS)));
    const newGridY = Math.max(0, Math.min(ROWS - gridH, snapToGrid(startGridY * (rootH / ROWS) + dy, rootH, ROWS)));

    applyGridPos(el, newGridX, newGridY, gridW, gridH);
  }

  function onMouseUp(e) {
    if (!dragState) return;
    const { el } = dragState;
    el.classList.remove('dragging');

    const gridX = +el.dataset.gridX;
    const gridY = +el.dataset.gridY;
    postMessage({ type: 'elementMoved', id: el.id, gridX, gridY });
    dragState = null;
  }

  // contenteditable のテキスト編集完了通知
  function onBlur(e) {
    const el = e.currentTarget;
    postMessage({ type: 'contentChanged', id: el.id, content: el.innerText });
  }

  // ダブルクリックで編集モード開始
  function onDblClick(e) {
    const el = e.currentTarget;
    el.contentEditable = 'true';
    el.focus();
  }

  function onFocusOut(e) {
    const el = e.currentTarget;
    el.contentEditable = 'false';
    onBlur(e);
  }

  // ── Public API ──────────────────────────────────────

  return {
    enableEditMode() {
      const root = document.getElementById('slide-root');
      if (!root) return;
      root.classList.add('edit-mode');

      document.querySelectorAll('.gcb-element').forEach((el) => {
        el.addEventListener('mousedown', onMouseDown);
        el.addEventListener('dblclick', onDblClick);
        el.addEventListener('focusout', onFocusOut);
      });

      document.addEventListener('mousemove', onMouseMove);
      document.addEventListener('mouseup', onMouseUp);

      // スライド背景クリックで選択解除
      root.addEventListener('mousedown', (e) => {
        if (e.target === root) selectElement(null);
      });
    },

    // C# から呼ばれる: 要素を指定グリッド位置へ移動
    moveElement(id, gridX, gridY) {
      const el = document.getElementById(id);
      if (!el) return;
      applyGridPos(el, gridX, gridY, +el.dataset.gridW, +el.dataset.gridH);
    },

    // C# から呼ばれる: 要素をリサイズ
    resizeElement(id, gridW, gridH) {
      const el = document.getElementById(id);
      if (!el) return;
      applyGridPos(el, +el.dataset.gridX, +el.dataset.gridY, gridW, gridH);
    },
  };
})();
