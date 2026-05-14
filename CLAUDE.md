# プロジェクト規約: C# WinUI 3 (x64) 開発ハイブリッド環境

# 環境構成
- 開発・解析: Manjaro Linux (VS Code + Cline via SSH)
- ビルド検証: Linux側の `dotnet` CLI を使用
- デバッグ・実行: Windows (Visual Studio)
- プロジェクトフォルダ: Linux Native File System 上に配置

# ビルド・テスト要件
- 全ての変更は x64 プラットフォームでのビルド成功を最終要件とする。
- 開発中の検証は `dotnet build -c Debug -r win-x64` を基本とし、コンテキスト消費を抑制する。
- Linux上では WinUI 3 特有の `MSB3073` (XamlCompiler.exe) エラーが発生することを既知の仕様として許容する。
- Linux側での「パス（成功）」の定義：
    1. `dotnet restore -r win-x64` が正常に完了すること。
    2. C#ソースコード（.cs）において、型解決エラー（未定義のクラス等）が発生していないこと。
- 警告 (Warning) は原則修正するが、技術的に妥当な理由がある場合は許容する（理由は明示すること）。

# コミュニケーション
- 日本語で応答する（コード・変数名は英語）。
- 簡潔に回答し、自明な説明は省略する。
- 複雑な実装（新規機能や大規模な修正）の前に計画を提示し、承認後に着手する。

# コードスタイル
- C# 9.0+ の関数型アプローチ（LINQ, record, readonly）を優先し、副作用を最小化する。
- `dynamic` や不適切な `object` は避け、厳密な型付け（Generics活用）を徹底する。
- エラーは適切な例外型を使い、意味のあるメッセージを付与する。
- 意図的に警告を抑制する場合は、`#pragma` と共に理由をコメントする。

# Git規約
- Conventional Commits形式（日本語）を使用（例: `feat: XXX機能の追加`）。
- 自動コミット・自動push禁止。
- 作業ブランチ（`linux-env-dev`等）から `dev` への統合時は、事前にビルド整合性を確認する。

# 禁止事項
- READMEや既存のドキュメントを許可なく生成・変更しない。
- テストコードを相談なしに削除・コメントアウトしない。
- 既存の動作するコードを、リファクタリングのみを目的として理由なく変更しない。

# 推奨事項
不明点等は質問ツールを使って積極的に聞くこと

## 1. Think Before Coding

**Don't assume. Don't hide confusion. Surface tradeoffs.**

Before implementing:
- State your assumptions explicitly. If uncertain, ask.
- If multiple interpretations exist, present them - don't pick silently.
- If a simpler approach exists, say so. Push back when warranted.
- If something is unclear, stop. Name what's confusing. Ask.

## 2. Simplicity First

**Minimum code that solves the problem. Nothing speculative.**

- No features beyond what was asked.
- No abstractions for single-use code.
- No "flexibility" or "configurability" that wasn't requested.
- No error handling for impossible scenarios.
- If you write 200 lines and it could be 50, rewrite it.

Ask yourself: "Would a senior engineer say this is overcomplicated?" If yes, simplify.

## 3. Surgical Changes

**Touch only what you must. Clean up only your own mess.**

When editing existing code:
- Don't "improve" adjacent code, comments, or formatting.
- Don't refactor things that aren't broken.
- Match existing style, even if you'd do it differently.
- If you notice unrelated dead code, mention it - don't delete it.

When your changes create orphans:
- Remove imports/variables/functions that YOUR changes made unused.
- Don't remove pre-existing dead code unless asked.

The test: Every changed line should trace directly to the user's request.

## 4. Goal-Driven Execution

**Define success criteria. Loop until verified.**

Transform tasks into verifiable goals:
- "Add validation" → "Write tests for invalid inputs, then make them pass"
- "Fix the bug" → "Write a test that reproduces it, then make it pass"
- "Refactor X" → "Ensure tests pass before and after"

For multi-step tasks, state a brief plan:
```
1. [Step] → verify: [check]
2. [Step] → verify: [check]
3. [Step] → verify: [check]
```

Strong success criteria let you loop independently. Weak criteria ("make it work") require constant clarification.

---
