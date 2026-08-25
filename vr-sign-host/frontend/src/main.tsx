import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import App from './App'
import InteractionStudyApp from './InteractionStudyApp'
import './styles.css'

const interactionMode = new URLSearchParams(window.location.search).get('mode') === 'interaction'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    {interactionMode ? <InteractionStudyApp /> : <App />}
  </StrictMode>,
)

