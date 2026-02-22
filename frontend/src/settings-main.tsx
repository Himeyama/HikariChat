import React from 'react';
import ReactDOM from 'react-dom/client';
import SettingsApp from './SettingsApp.tsx';
import '../src/index.css';
import { Theme } from '@radix-ui/themes';
import '@radix-ui/themes/styles.css';

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <Theme>
      <SettingsApp />
    </Theme>
  </React.StrictMode>,
);
