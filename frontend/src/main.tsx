import React from 'react';
import ReactDOM from 'react-dom/client';
import App from './App.tsx';
import './index.css';
import { Theme } from '@radix-ui/themes';
import '@radix-ui/themes/styles.css';
import './theme.css';

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <Theme accentColor='green'>
      <App />
    </Theme>
  </React.StrictMode>,
);
