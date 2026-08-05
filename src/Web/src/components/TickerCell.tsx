/**
 * The Asset cell on both tables: the symbol, and the company name under it when there is
 * one.
 *
 * A MISSING NAME IS NOT AN ERROR STATE — it renders as the ticker alone, with no dash, no
 * placeholder and no muted "unknown". `NO_VALUE` would be wrong here: it means "this
 * figure has not arrived", and a name that was never cached is not a figure that is late.
 * Every holding added before ticker search existed has no name, including rows in the
 * deployed database, so the nameless row is the ordinary case rather than the degraded one.
 *
 * A `<span>` rather than a `<div>` because `Table` renders each cell inside a `<span>` in
 * its mobile card layout.
 */
export function TickerCell({ ticker, name }: { ticker: string; name: string | null }) {
  return (
    <span className="flex flex-col gap-0.5">
      <span className="text-tx">{ticker}</span>
      {name ? <span className="text-mu text-[11px] leading-tight">{name}</span> : null}
    </span>
  )
}
