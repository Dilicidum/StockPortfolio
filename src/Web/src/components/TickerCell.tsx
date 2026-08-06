export function TickerCell({ ticker, name }: { ticker: string; name: string | null }) {
  return (
    <span className="flex flex-col gap-0.5">
      <span className="text-tx">{ticker}</span>
      {name ? <span className="text-mu text-[11px] leading-tight">{name}</span> : null}
    </span>
  )
}
