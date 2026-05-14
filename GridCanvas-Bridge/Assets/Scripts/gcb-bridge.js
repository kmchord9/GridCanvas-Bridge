'use strict';

const GCB = (() => {
  const COLS = 24;
  const ROWS = 24;

  let selectedEl = null;
  let editingEl = null;
  let dragState = null;
  let activeRoot = null;       // 現在 edit-mode が有効なスライドルート
  let globalRegistered = false; // mousemove / mouseup / keydown の二重登録防止

  // ── グリッド計算 ─────────────────────────────────────

  const toPctX = (g) => (g / COLS * 100).toFixed(4) + '%';
  const toPctY = (g) => (g / ROWS * 100).toFixed(4) + '%';

  function snapToGrid(px, containerSize, cells) {
    const cellSize = containerSize / cells;
    return Math.max(0, Math.min(cells - 1, Math.round(px / cellSize)));
  }

  function applyGridPos(el, gridX, gridY, gridW, gridH) {
    el.style.left   = toPctX(gridX);
    el.style.top    = toPctY(gridY);
    el.style.width  = toPctX(gridW);
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

  // ── 選択 ───────────────────────────────────────────────

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

  // ── テキスト編集モード ─────────────────────────────────

  function enterEditMode(el, clientX, clientY) {
    if (editingEl === el) return;
    exitEditMode();
    editingEl = el;
    el.contentEditable = 'true';
    el.classList.add('editing');
    el.focus();

    if (clientX !== undefined && document.caretRangeFromPoint) {
      const range = document.caretRangeFromPoint(clientX, clientY);
      if (range) {
        const sel = window.getSelection();
        sel.removeAllRanges();
        sel.addRange(range);
      }
    }
  }

  function exitEditMode() {
    if (!editingEl) return;
    const el = editingEl;
    editingEl = null;
    el.contentEditable = 'false';
    el.classList.remove('editing');
    postMessage({ type: 'contentChanged', id: el.id, content: el.innerText.trim() });
  }

  // ── ドラッグ & ドロップ ────────────────────────────────

  function onMouseDown(e) {
    const el = e.currentTarget;
    if (el.contentEditable === 'true') return;
    if (e.target !== el) { selectElement(el); return; }

    e.preventDefault();
    selectElement(el);

    const root = el.closest('#slide-root, .slide-root') || activeRoot;
    if (!root) return;
    const rect = root.getBoundingClientRect();
    dragState = {
      el,
      startMouseX: e.clientX, startMouseY: e.clientY,
      startGridX: +el.dataset.gridX, startGridY: +el.dataset.gridY,
      gridW: +el.dataset.gridW, gridH: +el.dataset.gridH,
      rootW: rect.width, rootH: rect.height,
    };
    el.classList.add('dragging');
  }

  function onMouseMove(e) {
    if (!dragState) return;
    const { el, startMouseX, startMouseY, startGridX, startGridY, gridW, gridH, rootW, rootH } = dragState;
    const dx = e.clientX - startMouseX;
    const dy = e.clientY - startMouseY;
    const newX = Math.max(0, Math.min(COLS - gridW, snapToGrid(startGridX * (rootW / COLS) + dx, rootW, COLS)));
    const newY = Math.max(0, Math.min(ROWS - gridH, snapToGrid(startGridY * (rootH / ROWS) + dy, rootH, ROWS)));
    applyGridPos(el, newX, newY, gridW, gridH);
  }

  function onMouseUp() {
    if (!dragState) return;
    const { el } = dragState;
    el.classList.remove('dragging');
    postMessage({ type: 'elementMoved', id: el.id, gridX: +el.dataset.gridX, gridY: +el.dataset.gridY });
    dragState = null;
  }

  // ── 要素イベントハンドラ ────────────────────────────────

  function onDblClick(e) {
    e.stopPropagation();
    enterEditMode(e.currentTarget, e.clientX, e.clientY);
  }

  function onFocusOut(e) {
    if (e.currentTarget.contains(e.relatedTarget)) return;
    exitEditMode();
  }

  // ── アクティブスライドへの edit-mode 適用 ──────────────

  function activateRoot(root) {
    // 前のルートから edit-mode を除去
    if (activeRoot && activeRoot !== root) {
      activeRoot.classList.remove('edit-mode');
    }
    activeRoot = root;
    root.classList.add('edit-mode');

    // 要素のイベントを再登録（重複を防ぐため先に除去）
    root.querySelectorAll('.gcb-element').forEach((el) => {
      el.removeEventListener('mousedown', onMouseDown);
      el.removeEventListener('dblclick',  onDblClick);
      el.removeEventListener('focusout',  onFocusOut);
      el.addEventListener('mousedown', onMouseDown);
      el.addEventListener('dblclick',  onDblClick);
      el.addEventListener('focusout',  onFocusOut);
    });

    // ルート背景クリックで選択・編集を解除（重複防止）
    root.removeEventListener('mousedown', onRootBackground);
    root.addEventListener('mousedown', onRootBackground);
  }

  function onRootBackground(e) {
    if (e.target === e.currentTarget) {
      exitEditMode();
      selectElement(null);
    }
  }

  // アクティブなスライドルートを探す
  // テンプレート: #slide-root
  // 外部マルチスライド HTML: .gcb-slide.active .slide-root か .slide-root
  function findActiveRoot() {
    return document.getElementById('slide-root')
        || document.querySelector('.gcb-slide.active .slide-root')
        || document.querySelector('.slide-root');
  }

  // ── Public API ──────────────────────────────────────────

  return {
    /**
     * 編集モードを有効化する。
     * ファイルを開くとき・スライド切り替え時に C# から都度呼ばれる。
     */
    enableEditMode() {
      // ① プレゼンナビゲーション（クリックゾーン・ボタン）を無効化
      document.getElementById('gcb-presentation')?.classList.add('gcb-editing');

      // ② グローバルイベントは一度だけ登録
      if (!globalRegistered) {
        globalRegistered = true;
        document.addEventListener('mousemove', onMouseMove);
        document.addEventListener('mouseup',   onMouseUp);
        document.addEventListener('keydown', (e) => {
          if (e.key === 'Escape') exitEditMode();
        });
      }

      // ③ アクティブなスライドルートを取得して編集対象を切り替える
      const root = findActiveRoot();
      if (!root) return;
      activateRoot(root);
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
