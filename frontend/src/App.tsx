import { useState } from 'react'
import reactLogo from './assets/react.svg'
import rhinoLogo from './assets/rhino.png'
import revitLogo from './assets/revit.svg'
import viteLogo from '/vite.svg'
import './App.css'

function App() {
  const [count, setCount] = useState(0)

  const host: string = "Rhino"
  let logo: string = reactLogo
  switch (host) {
    case 'Rhino':
      logo = rhinoLogo
      break;
    case 'Revit':
      logo = revitLogo
      break;
    case 'React':
    default:
      logo = reactLogo
      break;
  }

  return (
    <>
      <div>
        <a href="https://vite.dev" target="_blank">
          <img src={viteLogo} className="logo" alt="Vite logo" />
        </a>
        <a href="https://react.dev" target="_blank">
          <img src={logo} className="logo react" alt="Host logo" />
        </a>
      </div>
      <h1>Vite + {host}</h1>
      <div className="card">
        <button onClick={() => setCount((count) => count + 1)}>
          count is {count}
        </button>
        <p>
          Edit <code>src/App.tsx</code> and save to test HMR
        </p>
      </div>
      <p className="read-the-docs">
        Click on the Vite and React logos to learn more
      </p>
    </>
  )
}

export default App
