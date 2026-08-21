import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import App from './App'
import { ReviewApp } from './review/ReviewApp'
import './styles.css'

const reviewMode = window.location.pathname.startsWith('/review')
document.title = reviewMode ? '手语数据审核' : 'VR 手语录制台'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    {reviewMode ? <ReviewApp /> : <App />}
  </StrictMode>,
)
