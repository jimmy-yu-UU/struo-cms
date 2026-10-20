try{var s=localStorage.getItem('struo.theme');if(s==='dark'||(s!=='light'&&matchMedia('(prefers-color-scheme: dark)').matches))document.documentElement.classList.add('app-dark')}catch(e){}
