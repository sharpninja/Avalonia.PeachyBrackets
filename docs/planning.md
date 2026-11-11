# Planning

- Create implementation of Avalonia Browser control based on Avalonia's interface.
  - Status: plan only. Not started. Blocked on the prerequisites below.
  - Open prerequisites:
    - DOM: PeachBrackets builds an internal `CssBox` tree and exposes only `DocumentElement` handles plus attribute, style and class mutation. A public, standards shaped DOM (Document, Element, Node, events) is needed.
    - Script engine: PeachBrackets ignores `<script>`. A JS or WASM engine must be chosen and integrated, including DOM bindings.
    - Relayout on mutation: script driven DOM changes must trigger incremental relayout and repaint.
  - Decisions needed before any work: where the DOM lives (upstream in PeachBrackets or in this library), which script engine, and the requirements and BDPv4 plan for approval.
