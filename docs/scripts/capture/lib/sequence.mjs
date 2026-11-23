// Runs an async function over a list strictly one item after another: the next call starts only
// when the previous one has finished. The steps of a run depend on each other's responses, so they
// never overlap; this is the one place that says so.
export function inOrder(items, run) {
  return items.reduce((chain, item, index) => chain.then(() => run(item, index)), Promise.resolve())
}
