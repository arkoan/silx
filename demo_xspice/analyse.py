import numpy as np, matplotlib; matplotlib.use("Agg"); import matplotlib.pyplot as plt
cols=['in','cmp','a_cmp','q0','q1','q2','full','led']
def load(f):
    d=np.loadtxt(f); return d[:,0],{c:d[:,2*i+1] for i,c in enumerate(cols)}
def edges(t,x,th=2.5):
    s=x>th; return t[1:][s[1:]&~s[:-1]]
fig,ax=plt.subplots(3,2,figsize=(13,8),sharex='col')
for j,(f,n) in enumerate([('sortie_hyst.txt','avec hystérésis'),('sortie_sans_hyst.txt','sans hystérésis')]):
    t,v=load(f); t*=1e3
    ax[0,j].plot(t,v['in'],lw=.6,label='in (bruité)'); ax[0,j].plot(t,v['cmp'],label='sortie comparateur'); ax[0,j].set_title(n); ax[0,j].legend(loc='upper right',fontsize=7)
    ax[1,j].plot(t,v['a_cmp'],lw=.8,label='d_cmp'); ax[1,j].legend(fontsize=7)
    for k,c in enumerate(['q0','q1','q2']): ax[2,j].plot(t,v[c]+6*(2-k),label=c)
    ax[2,j].plot(t,v['full']-6,label='full'); ax[2,j].legend(fontsize=7,ncol=4)
    print(n,'fronts montants comparateur:',len(edges(t,v['a_cmp'])),'| compteur: valeur finale',int(v['q0'][-1]>2.5)+2*int(v['q1'][-1]>2.5)+4*int(v['q2'][-1]>2.5))
ax[2,0].set_xlabel('t (ms)'); ax[2,1].set_xlabel('t (ms)'); plt.tight_layout(); plt.savefig('resultats.png',dpi=110)
