import { Component, type ReactNode } from 'react'

export interface ErrorBoundaryFallbackProps {
  error: Error
  retry: () => void
}

export interface ErrorBoundaryProps {
  fallback: (props: ErrorBoundaryFallbackProps) => ReactNode
  children: ReactNode
}

interface ErrorBoundaryState {
  error: Error | null
}

export class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  constructor(props: ErrorBoundaryProps) {
    super(props)
    this.state = { error: null }
  }

  static getDerivedStateFromError(thrown: unknown): ErrorBoundaryState {
    return { error: thrown instanceof Error ? thrown : new Error(String(thrown)) }
  }

  retry = (): void => {
    this.setState({ error: null })
  }

  override render(): ReactNode {
    const { error } = this.state

    if (error) return this.props.fallback({ error, retry: this.retry })

    return this.props.children
  }
}
