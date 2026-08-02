export interface SpinnerProps {
  size?: number
  label?: string
}

/**
 * A CSS ring, not an SVG library. The keyframe `tz-spin` lives in index.css.
 * `role="status"` + a visually-hidden label is what makes it announce; the ring
 * itself is aria-hidden so it is not read as an image.
 */
export function Spinner({ size = 16, label }: SpinnerProps) {
  return (
    <span role="status" className="inline-flex items-center gap-2">
      <span
        aria-hidden="true"
        style={{
          width: size,
          height: size,
          borderWidth: Math.max(2, Math.round(size / 8)),
          animation: 'tz-spin 0.7s linear infinite',
        }}
        className="inline-block rounded-full border-bd border-t-ac"
      />
      {label ? <span className="text-mu text-xs">{label}</span> : <span className="sr-only">Loading</span>}
    </span>
  )
}
