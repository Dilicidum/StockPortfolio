export function Logo({ size = 20 }: { size?: number }) {
  return (
    <span className="flex items-center gap-2.5">
      <span
        aria-hidden="true"
        style={{ width: size, height: size }}
        className="rounded-[5px] bg-ac"
      />
      <span className="font-semibold tracking-[-0.01em]">StockPortfolio</span>
    </span>
  )
}
