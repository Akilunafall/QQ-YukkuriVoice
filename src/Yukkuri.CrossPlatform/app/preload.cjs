'use strict';
const { contextBridge, ipcRenderer } = require('electron');
contextBridge.exposeInMainWorld('yukkuri', {
    state: () => ipcRenderer.invoke('state'),
    edit: changes => ipcRenderer.invoke('edit', changes),
    generate: () => ipcRenderer.invoke('generate'),
    floating: () => ipcRenderer.invoke('floating'),
    mainWindow: () => ipcRenderer.invoke('main-window'),
    export: () => ipcRenderer.invoke('export'),
    useHistory: id => ipcRenderer.invoke('history-use', id),
    deleteHistory: id => ipcRenderer.invoke('history-delete', id),
    importEngine: () => ipcRenderer.invoke('import-engine'),
    openData: () => ipcRenderer.invoke('open-data'),
    refreshQQ: () => ipcRenderer.invoke('qq-refresh'),
    launchQQ: () => ipcRenderer.invoke('qq-launch'),
    sendQQ: () => ipcRenderer.invoke('qq-send'),
    onState: callback => {
        const listener = (_event, state) => callback(state);
        ipcRenderer.on('state', listener);
        return () => ipcRenderer.removeListener('state', listener);
    }
});
