import React from 'react'
import { createRoot } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import '@fontsource/dm-sans/400.css'
import '@fontsource/dm-sans/500.css'
import '@fontsource/dm-sans/600.css'
import '@fontsource/newsreader/400.css'
import '@fontsource/newsreader/400-italic.css'
import '@fontsource/jetbrains-mono/400.css'
import '../styles.css'
import { App } from '../App'

const client = new QueryClient({
  defaultOptions: {
    queries: { retry: false, staleTime: 30_000, refetchOnWindowFocus: false },
    mutations: { retry: false },
  },
})
createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <QueryClientProvider client={client}>
      <App />
    </QueryClientProvider>
  </React.StrictMode>,
)
