/**
 * GenHub Native Desktop Client Simulation (Avalonia XAML Engine Parity)
 * 1:1 Flow, Modals, State Handling, Notifications, Diagnostics
 */
(function() {
    'use strict';

    // Global Toast Notification Helper
    window.showGenHubToast = function(type, title, desc) {
        const container = document.getElementById('ghToastContainer');
        if (!container) return;

        const toast = document.createElement('div');
        toast.className = `genhub-toast ${type.toLowerCase()}`;
        
        let iconSymbol = 'ℹ';
        if (type.toLowerCase() === 'success') iconSymbol = '✓';
        if (type.toLowerCase() === 'warning') iconSymbol = '⚠';

        toast.innerHTML = `
            <div class="genhub-toast-icon">${iconSymbol}</div>
            <div class="genhub-toast-body">
                <div class="genhub-toast-title">${title}</div>
                <div class="genhub-toast-desc">${desc}</div>
            </div>
            <button class="genhub-toast-close" aria-label="Dismiss">✕</button>
        `;

        toast.querySelector('.genhub-toast-close').addEventListener('click', () => {
            toast.style.opacity = '0';
            toast.style.transform = 'translateX(20px)';
            setTimeout(() => toast.remove(), 180);
        });

        container.appendChild(toast);

        // Auto dismiss after 4 seconds
        setTimeout(() => {
            if (toast.parentNode) {
                toast.style.opacity = '0';
                toast.style.transform = 'translateX(20px)';
                setTimeout(() => toast.remove(), 180);
            }
        }, 4000);
    };

    // Modal Control Helpers
    function openModal(id) {
        const modal = document.getElementById(id);
        if (modal) {
            modal.classList.add('active');
        }
    }

    function closeModal(id) {
        const modal = document.getElementById(id);
        if (modal) {
            modal.classList.remove('active');
        }
    }

    document.querySelectorAll('[data-close-modal]').forEach(btn => {
        btn.addEventListener('click', (e) => {
            e.stopPropagation();
            const targetId = btn.getAttribute('data-close-modal');
            closeModal(targetId);
        });
    });

    document.querySelectorAll('.gh-modal-backdrop').forEach(modal => {
        modal.addEventListener('click', (e) => {
            if (e.target === modal) {
                modal.classList.remove('active');
            }
        });
    });

    // Navigation Tabs Switching (Game Profiles, Downloads, Tools, Settings, Info)
    const pillButtons = document.querySelectorAll('.genhub-pill-btn');
    const panes = {
        'profiles': document.getElementById('gh-pane-profiles'),
        'downloads': document.getElementById('gh-pane-downloads'),
        'tools': document.getElementById('gh-pane-tools'),
        'settings': document.getElementById('gh-pane-settings'),
        'info': document.getElementById('gh-pane-info')
    };

    function switchViewPane(paneKey) {
        pillButtons.forEach(btn => {
            const isTarget = btn.getAttribute('data-pane') === paneKey;
            btn.classList.toggle('active', isTarget);
            btn.setAttribute('aria-selected', isTarget ? 'true' : 'false');
        });

        // Hide notification flyout if open
        const notifFlyout = document.getElementById('ghNotificationFlyout');
        if (notifFlyout) notifFlyout.classList.remove('active');

        // Toggle titlebar buttons active states
        const infoBtn = document.getElementById('ghTbBtnInfo');
        const settingsBtn = document.getElementById('ghTbBtnSettings');
        if (infoBtn) infoBtn.classList.toggle('active', paneKey === 'info');
        if (settingsBtn) settingsBtn.classList.toggle('active', paneKey === 'settings');

        Object.keys(panes).forEach(k => {
            if (panes[k]) {
                panes[k].classList.toggle('active', k === paneKey);
            }
        });

        // Update statusbar text
        const statusText = document.getElementById('ghStatusText');
        if (statusText) {
            if (paneKey === 'profiles') statusText.textContent = 'Ready • Workspaces Synced (NTFS Hardlinks Active)';
            else if (paneKey === 'downloads') statusText.textContent = 'Downloads Browser • Connected to Community Catalog';
            else if (paneKey === 'tools') statusText.textContent = 'Diagnostics & Replay Analyzer Initialized';
            else if (paneKey === 'settings') statusText.textContent = 'Configuration Loaded (~/.config/GenHub/settings.json)';
            else if (paneKey === 'info') statusText.textContent = 'Documentation & Frequently Asked Questions';
        }
    }

    pillButtons.forEach(btn => {
        btn.addEventListener('click', () => {
            const paneKey = btn.getAttribute('data-pane');
            switchViewPane(paneKey);
        });
    });

    // Titlebar Action Buttons
    const infoTbBtn = document.getElementById('ghTbBtnInfo');
    if (infoTbBtn) {
        infoTbBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            switchViewPane('info');
        });
    }

    const settingsTbBtn = document.getElementById('ghTbBtnSettings');
    if (settingsTbBtn) {
        settingsTbBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            switchViewPane('settings');
        });
    }

    // Notification Bell & Flyout
    const notifTbBtn = document.getElementById('ghTbBtnNotifications');
    const notifFlyout = document.getElementById('ghNotificationFlyout');
    const notifBadge = document.getElementById('ghNotifBadge');

    if (notifTbBtn && notifFlyout) {
        notifTbBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            const isActive = notifFlyout.classList.toggle('active');
            if (isActive && notifBadge) {
                notifBadge.style.display = 'none';
            }
        });

        document.addEventListener('click', (e) => {
            if (!notifFlyout.contains(e.target) && e.target !== notifTbBtn) {
                notifFlyout.classList.remove('active');
            }
        });
    }

    const clearNotifsBtn = document.getElementById('ghClearNotifsBtn');
    if (clearNotifsBtn) {
        clearNotifsBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            const list = document.getElementById('ghNotifList');
            if (list) {
                list.innerHTML = `
                    <div style="padding: 24px 16px; text-align: center; color: #64748b; font-size: 12px;">
                        No pending notifications
                    </div>
                `;
            }
            if (notifBadge) notifBadge.style.display = 'none';
            window.showGenHubToast('Info', 'Notifications Cleared', 'All feed alerts have been marked as read.');
        });
    }

    // Window controls
    document.querySelectorAll('.gh-win-btn.close').forEach(btn => {
        btn.addEventListener('click', () => {
            window.showGenHubToast('Warning', 'Close Attempt', 'Desktop client minimizes to system tray when closed.');
        });
    });

    // Profile Card State and Context Management
    let activeCardEditing = null;
    let isCreatingProfile = false;

    function wireProfileCard(card) {
        // Play / Stop Launch Button
        const launchBtn = card.querySelector('.gh-launch-btn');
        if (launchBtn) {
            launchBtn.addEventListener('click', (e) => {
                e.stopPropagation();
                const pName = card.getAttribute('data-name') || 'Zero Hour';
                const isRunning = card.classList.contains('active-profile');
                const textSpan = launchBtn.querySelector('.launch-text');

                if (!isRunning) {
                    document.querySelectorAll('.gh-profile-card').forEach(c => c.classList.remove('active-profile'));
                    card.classList.add('active-profile');
                    if (textSpan) textSpan.textContent = 'STOP';
                    const statusText = document.getElementById('ghStatusText');
                    if (statusText) statusText.textContent = `Running: ${pName} (PID: 14820)`;
                    window.showGenHubToast('Success', 'Game Launched', `Process started for "${pName}". DirectDraw and GenTool injected.`);
                } else {
                    card.classList.remove('active-profile');
                    if (textSpan) textSpan.textContent = 'LAUNCH';
                    const statusText = document.getElementById('ghStatusText');
                    if (statusText) statusText.textContent = 'Ready • Workspaces Synced (NTFS Hardlinks Active)';
                    window.showGenHubToast('Info', 'Process Stopped', `Closed "${pName}". Hardlink workspace intact.`);
                }
            });
        }

        // Steam Integration Button
        const steamBtn = card.querySelector('.steam-btn');
        if (steamBtn) {
            steamBtn.addEventListener('click', (e) => {
                e.stopPropagation();
                const pName = card.getAttribute('data-name') || 'Profile';
                window.showGenHubToast('Info', 'Steam Integration', `Connected "${pName}" with Steam Overlay & Friends Broadcast (AppID: 24860).`);
            });
        }

        // Edit Profile Settings -> Opens GameProfileSettingsWindow
        const editBtn = card.querySelector('.edit-btn');
        if (editBtn) {
            editBtn.addEventListener('click', (e) => {
                e.stopPropagation();
                activeCardEditing = card;
                isCreatingProfile = false;
                const pName = card.getAttribute('data-name') || 'Shockwave 1.201';
                
                const titleEl = document.getElementById('ghWinProfileTitle');
                const nameInput = document.getElementById('ghProfileNameInput');
                if (titleEl) titleEl.textContent = pName;
                if (nameInput) nameInput.value = pName;

                openModal('ghGameProfileSettingsWindow');
            });
        }

        // Clone Profile
        const cloneBtn = card.querySelector('.clone-btn');
        if (cloneBtn) {
            cloneBtn.addEventListener('click', (e) => {
                e.stopPropagation();
                const pName = card.getAttribute('data-name') || 'Profile';
                const clone = card.cloneNode(true);
                clone.setAttribute('data-name', `${pName} (Copy)`);
                clone.classList.remove('active-profile');
                const titleEl = clone.querySelector('.gh-card-title');
                if (titleEl) titleEl.textContent = `${pName} (Copy)`;
                
                wireProfileCard(clone);
                const list = document.getElementById('ghProfilesList');
                const addCard = document.getElementById('ghAddNewProfileBtn');
                list.insertBefore(clone, addCard);
                updateProfilesCount();
                window.showGenHubToast('Success', 'Profile Cloned', `Created isolated copy: "${pName} (Copy)".`);
            });
        }

        // Shortcut Button
        const shortcutBtn = card.querySelector('.shortcut-btn');
        if (shortcutBtn) {
            shortcutBtn.addEventListener('click', (e) => {
                e.stopPropagation();
                const pName = card.getAttribute('data-name') || 'Profile';
                window.showGenHubToast('Success', 'Shortcut Created', `Added desktop launcher icon for "${pName}".`);
            });
        }

        // Delete Profile
        const delBtn = card.querySelector('.delete-btn');
        if (delBtn) {
            delBtn.addEventListener('click', (e) => {
                e.stopPropagation();
                const pName = card.getAttribute('data-name') || 'Profile';
                card.remove();
                updateProfilesCount();
                window.showGenHubToast('Warning', 'Profile Removed', `Deleted "${pName}" workspace from disk.`);
            });
        }
    }

    document.querySelectorAll('.gh-profile-card:not(.add-card)').forEach(wireProfileCard);

    function updateProfilesCount() {
        const count = document.querySelectorAll('.gh-profile-card:not(.add-card)').length;
        const countEl = document.getElementById('ghProfilesCount');
        if (countEl) countEl.textContent = `Loaded ${count} profiles`;
    }

    // Add New Profile Card -> Also opens GameProfileSettingsWindow
    const addProfileBtn = document.getElementById('ghAddNewProfileBtn');
    if (addProfileBtn) {
        addProfileBtn.addEventListener('click', () => {
            activeCardEditing = null;
            isCreatingProfile = true;
            const titleEl = document.getElementById('ghWinProfileTitle');
            const nameInput = document.getElementById('ghProfileNameInput');
            if (titleEl) titleEl.textContent = 'New Profile';
            if (nameInput) nameInput.value = 'Custom Profile';
            openModal('ghGameProfileSettingsWindow');
        });
    }

    // GameProfileSettingsWindow Sub-navigation (Content, Profile Settings, Game Settings)
    const winNavBtns = document.querySelectorAll('.gh-win-nav-btn');
    const winPanels = [
        document.getElementById('ghWinTabContent'),
        document.getElementById('ghWinTabGeneral'),
        document.getElementById('ghWinTabGame')
    ];

    winNavBtns.forEach((btn, idx) => {
        btn.addEventListener('click', () => {
            winNavBtns.forEach(b => {
                b.classList.remove('active');
                b.setAttribute('aria-selected', 'false');
            });
            btn.classList.add('active');
            btn.setAttribute('aria-selected', 'true');

            winPanels.forEach((p, pIdx) => {
                if (p) p.classList.toggle('active', pIdx === idx);
            });
        });
    });

    // General Settings Sub-sidebar (Identity, Appearance, Launch, Theme)
    const subNavBtns = document.querySelectorAll('.gh-sub-nav-btn');
    const subPanels = {
        'identity': document.getElementById('ghSubIdentity'),
        'appearance': document.getElementById('ghSubAppearance'),
        'launch': document.getElementById('ghSubLaunch'),
        'theme': document.getElementById('ghSubTheme')
    };

    subNavBtns.forEach(btn => {
        btn.addEventListener('click', () => {
            const cat = btn.getAttribute('data-sub-category');
            subNavBtns.forEach(b => b.classList.remove('active'));
            btn.classList.add('active');

            Object.keys(subPanels).forEach(k => {
                if (subPanels[k]) subPanels[k].classList.toggle('active', k === cat);
            });
        });
    });

    // Window Randomize Color Button
    const randColorBtn = document.getElementById('ghWinRandomizeColorBtn');
    if (randColorBtn) {
        randColorBtn.addEventListener('click', () => {
            const colors = ['#8b5cf6', '#10b981', '#3b82f6', '#f59e0b', '#ef4444', '#06b6d4'];
            const randomColor = colors[Math.floor(Math.random() * colors.length)];
            applyThemeAccent(randomColor);
            window.showGenHubToast('Info', 'Accent Randomized', `Applied highlight color: ${randomColor}`);
        });
    }

    // Window Fullscreen Button
    const fsBtn = document.getElementById('ghWinFullscreenBtn');
    if (fsBtn) {
        fsBtn.addEventListener('click', () => {
            const win = document.querySelector('.gh-profile-settings-window');
            if (win) {
                const isMax = win.classList.toggle('fullscreen-max');
                if (isMax) {
                    win.style.maxWidth = '100vw';
                    win.style.width = '98vw';
                    win.style.height = '94vh';
                    win.style.maxHeight = '96vh';
                } else {
                    win.style.maxWidth = '95vw';
                    win.style.width = '880px';
                    win.style.height = '82vh';
                    win.style.maxHeight = '720px';
                }
            }
        });
    }

    // Save Profile FAB Button
    const saveFabBtn = document.getElementById('ghSaveProfileFabBtn');
    if (saveFabBtn) {
        saveFabBtn.addEventListener('click', () => {
            const nameInput = document.getElementById('ghProfileNameInput');
            const name = nameInput ? nameInput.value.trim() || 'Custom Profile' : 'Custom Profile';

            if (isCreatingProfile) {
                // Create a new card in the profile list
                const newCard = document.createElement('div');
                newCard.className = 'gh-profile-card';
                newCard.setAttribute('data-name', name);
                newCard.innerHTML = `
                    <img src="./assets/images/zerohour-cover.png" alt="Profile Cover" class="gh-card-bg">
                    <div class="gh-card-gradient"></div>
                    <div class="gh-running-badge"><span class="gh-running-dot"></span> RUNNING</div>
                    <div class="gh-card-actions-bar">
                        <button class="gh-card-act-btn steam-btn" title="Steam Overlay">
                            <img src="./assets/icons/steam-icon.png" alt="Steam" class="gh-act-icon-img">
                        </button>
                        <button class="gh-card-act-btn edit-btn" title="Edit Profile Settings">
                            <svg viewBox="0 0 24 24" width="13" height="13" fill="currentColor"><path d="M20.71,4.04C21.1,3.65 21.1,3 20.71,2.63L18.37,0.29C18,-.1 17.35,-.1 16.96,0.29L15.12,2.12L18.87,5.87M3,17.25V21H6.75L17.81,9.93L14.06,6.18L3,17.25Z"/></svg>
                        </button>
                        <button class="gh-card-act-btn clone-btn" title="Duplicate Profile">
                            <svg viewBox="0 0 24 24" width="13" height="13" fill="currentColor"><path d="M19,21H8V7H19M19,5H8A2,2 0 0,0 6,7V21A2,2 0 0,0 8,23H19A2,2 0 0,0 21,21V7A2,2 0 0,0 19,5M16,1H4A2,2 0 0,0 2,3V17H4V3H16V1Z"/></svg>
                        </button>
                        <button class="gh-card-act-btn shortcut-btn" title="Create Desktop Shortcut">
                            <svg viewBox="0 0 24 24" width="13" height="13" fill="currentColor"><path d="M16,12V4H17V2H7V4H8V12L6,14V16H11.2V22H12.8V16H18V14L16,12M8.8,14L10,12.8V4H14V12.8L15.2,14H8.8Z"/></svg>
                        </button>
                        <button class="gh-card-act-btn delete-btn" title="Delete Profile">
                            <svg viewBox="0 0 24 24" width="13" height="13" fill="currentColor"><path d="M19,4H15.5L14.5,3H9.5L8.5,4H5V6H19M6,19A2,2 0 0,0 8,21H16A2,2 0 0,0 18,19V7H6V19Z"/></svg>
                        </button>
                    </div>
                    <div class="gh-card-hover">
                        <button class="gh-launch-btn">
                            <svg viewBox="0 0 24 24"><polygon points="5 3 19 12 5 21 5 3"></polygon></svg>
                            <span class="launch-text">LAUNCH</span>
                        </button>
                    </div>
                    <div class="gh-card-meta">
                        <div class="gh-card-info-row">
                            <div class="gh-card-badge-icon">
                                <img src="./assets/icons/generalshub-icon.png" alt="GenHub">
                            </div>
                            <div class="gh-card-texts">
                                <div class="gh-card-title">${name}</div>
                                <div class="gh-card-sub">Configured via Profile Settings</div>
                            </div>
                        </div>
                        <div class="gh-card-tags">
                            <span class="gh-tag">Active</span>
                            <span class="gh-tag">Zero Hour</span>
                        </div>
                    </div>
                `;
                wireProfileCard(newCard);
                const list = document.getElementById('ghProfilesList');
                list.insertBefore(newCard, addProfileBtn);
                updateProfilesCount();
                window.showGenHubToast('Success', 'Profile Created', `"${name}" configured with NTFS hardlinks.`);
            } else if (activeCardEditing) {
                // Update existing card
                activeCardEditing.setAttribute('data-name', name);
                const titleEl = activeCardEditing.querySelector('.gh-card-title');
                if (titleEl) titleEl.textContent = name;
                window.showGenHubToast('Success', 'Profile Saved', `Settings saved for "${name}".`);
            }

            closeModal('ghGameProfileSettingsWindow');
        });
    }

    // Add Local Content button inside Content Editor
    const addLocalBtn = document.getElementById('ghWinAddLocalContentBtn');
    if (addLocalBtn) {
        addLocalBtn.addEventListener('click', () => {
            window.showGenHubToast('Info', 'Local Content', 'Select a folder or BIG archive to link into this profile workspace.');
        });
    }

    // Scan Button
    const scanBtn = document.getElementById('ghScanBtn');
    if (scanBtn) {
        scanBtn.addEventListener('click', () => {
            const orig = scanBtn.textContent;
            scanBtn.textContent = 'SCANNING...';
            setTimeout(() => {
                scanBtn.textContent = orig;
                window.showGenHubToast('Info', 'Scan Complete', 'Discovered 4 local game installations across Steam & EA directories.');
            }, 600);
        });
    }

    // -------------------------------------------------------------
    // DOWNLOADS: PUBLISHER SWITCHING & CONTENT DETAIL VIEW
    // -------------------------------------------------------------
    const pubMeta = {
        'hackers': { title: 'TheSuperHackers', desc: 'Zero Hour engine patches, stability fixes, and modernized widescreen control bars.' },
        'online': { title: 'Generals Online', desc: 'Competitive multiplayer client, rank ladders, and NAT-traversal matchmaking services.' },
        'outpost': { title: 'CommunityOutpost', desc: 'Curated mod packages, balance tournaments, and high-fidelity texture upscales.' },
        'cnclabs': { title: 'CNC Labs', desc: 'Classic community map packs, mission campaigns, and level designer SDKs.' },
        'github': { title: 'GitHub Releases', desc: 'Open source builds, experimental test branches, and engine source code.' },
        'moddb': { title: 'ModDB Mirror', desc: 'Archived community legacy mods and total-conversion standalone releases.' }
    };

    let activePublisherKey = 'hackers';

    function filterDownloadsCatalog() {
        const q = (document.getElementById('ghCatalogSearch')?.value || '').toLowerCase().trim();
        const cat = document.getElementById('ghCatalogFilter')?.value || 'all';

        document.querySelectorAll('#ghCatalogGrid .gh-content-card').forEach(card => {
            const cardPub = card.getAttribute('data-pub') || '';
            const cardTitle = (card.getAttribute('data-title') || '').toLowerCase();
            const cardCat = card.getAttribute('data-category') || '';

            const matchesPub = cardPub === activePublisherKey;
            const matchesQuery = !q || cardTitle.includes(q);
            const matchesCat = cat === 'all' || cardCat === cat;

            card.style.display = (matchesPub && matchesQuery && matchesCat) ? '' : 'none';
        });
    }

    const pubBtns = document.querySelectorAll('.gh-pub-btn');
    pubBtns.forEach(btn => {
        btn.addEventListener('click', () => {
            const key = btn.getAttribute('data-pub');
            activePublisherKey = key;

            pubBtns.forEach(b => b.classList.remove('active'));
            btn.classList.add('active');

            if (pubMeta[key]) {
                const tEl = document.getElementById('ghActivePubTitle');
                const dEl = document.getElementById('ghActivePubDesc');
                if (tEl) tEl.textContent = pubMeta[key].title;
                if (dEl) dEl.textContent = pubMeta[key].desc;
            }

            // Return to browse view if inside detail view
            const browserView = document.getElementById('ghDownloadsBrowserView');
            const detailView = document.getElementById('ghContentDetailView');
            if (browserView) browserView.style.display = '';
            if (detailView) detailView.style.display = 'none';

            filterDownloadsCatalog();
            window.showGenHubToast('Info', 'Switched Publisher', `Viewing catalog from ${pubMeta[key]?.title || key}`);
        });
    });

    const catalogSearch = document.getElementById('ghCatalogSearch');
    const catalogFilter = document.getElementById('ghCatalogFilter');
    if (catalogSearch) catalogSearch.addEventListener('input', filterDownloadsCatalog);
    if (catalogFilter) catalogFilter.addEventListener('change', filterDownloadsCatalog);

    // Initial filter run
    filterDownloadsCatalog();

    // Content Detail View Transition
    function showContentDetail(contentName, card) {
        const browserView = document.getElementById('ghDownloadsBrowserView');
        const detailView = document.getElementById('ghContentDetailView');
        if (!detailView || !browserView) return;

        browserView.style.display = 'none';
        detailView.style.display = 'flex';

        // Populate detail fields
        const titleEl = document.getElementById('ghDetailTitle');
        const badgeEl = document.getElementById('ghDetailBadge');
        const authorEl = document.getElementById('ghDetailAuthor');
        const sizeEl = document.getElementById('ghDetailSize');
        const summaryTextEl = document.getElementById('ghDetailSummaryText');
        const iconEl = document.getElementById('ghDetailIconImg');

        if (card) {
            const cardTitle = card.querySelector('.gh-content-title')?.textContent || contentName;
            const cardDesc = card.querySelector('.gh-content-desc')?.textContent || '';
            const cardImg = card.querySelector('.gh-banner-bg')?.getAttribute('src') || './assets/images/zerohour-cover.png';
            const cardBadge = card.querySelector('.badge-tag')?.textContent || 'Package';

            if (titleEl) titleEl.textContent = cardTitle;
            if (badgeEl) badgeEl.textContent = cardBadge;
            if (summaryTextEl) summaryTextEl.textContent = cardDesc;
            if (iconEl) iconEl.src = cardImg;
            if (authorEl) authorEl.textContent = pubMeta[activePublisherKey]?.title || 'Community';
        }
    }

    document.querySelectorAll('.gh-card-details-btn').forEach(btn => {
        btn.addEventListener('click', (e) => {
            e.stopPropagation();
            const card = btn.closest('.gh-content-card');
            const contentName = btn.getAttribute('data-view-content') || 'Item';
            showContentDetail(contentName, card);
        });
    });

    // Also clicking the card body opens detail view
    document.querySelectorAll('.gh-content-card').forEach(card => {
        card.addEventListener('click', (e) => {
            if (e.target.closest('button') || e.target.closest('input')) return;
            const title = card.getAttribute('data-title') || 'Item';
            showContentDetail(title, card);
        });
    });

    const backToBrowseBtn = document.getElementById('ghBackToBrowseBtn');
    if (backToBrowseBtn) {
        backToBrowseBtn.addEventListener('click', () => {
            const browserView = document.getElementById('ghDownloadsBrowserView');
            const detailView = document.getElementById('ghContentDetailView');
            if (detailView) detailView.style.display = 'none';
            if (browserView) browserView.style.display = '';
        });
    }

    // Detail View Subtabs (Summary, Releases, Dependencies)
    document.querySelectorAll('[data-detail-tab]').forEach(btn => {
        btn.addEventListener('click', () => {
            const tabKey = btn.getAttribute('data-detail-tab');
            document.querySelectorAll('[data-detail-tab]').forEach(b => b.classList.remove('active'));
            btn.classList.add('active');

            const pSummary = document.getElementById('ghDetailPaneSummary');
            const pReleases = document.getElementById('ghDetailPaneReleases');
            const pDependencies = document.getElementById('ghDetailPaneDependencies');

            if (pSummary) pSummary.style.display = tabKey === 'summary' ? 'block' : 'none';
            if (pReleases) pReleases.style.display = tabKey === 'releases' ? 'block' : 'none';
            if (pDependencies) pDependencies.style.display = tabKey === 'dependencies' ? 'block' : 'none';
        });
    });

    // Detail Install Button
    const detailInstallBtn = document.getElementById('ghDetailInstallBtn');
    if (detailInstallBtn) {
        detailInstallBtn.addEventListener('click', () => {
            const profile = document.getElementById('ghDetailTargetProfile')?.value || 'Shockwave 1.201';
            const item = document.getElementById('ghDetailTitle')?.textContent || 'Package';
            window.showGenHubToast('Success', 'Linked to Profile', `Added "${item}" to workspace "${profile}" with 0 B duplication.`);
        });
    }

    // Add to Profile Buttons
    document.querySelectorAll('.gh-open-add-modal-btn').forEach(btn => {
        btn.addEventListener('click', (e) => {
            e.stopPropagation();
            const contentName = btn.getAttribute('data-content-name') || 'Item';
            const titleEl = document.getElementById('ghAddModalContentTitle');
            if (titleEl) titleEl.textContent = contentName;
            openModal('ghAddToProfileModal');
        });
    });

    document.querySelectorAll('.gh-sel-card:not(.create-new)').forEach(card => {
        card.addEventListener('click', () => {
            const pName = card.getAttribute('data-profile-name') || 'Profile';
            const cTitle = document.getElementById('ghAddModalContentTitle')?.textContent || 'Item';
            closeModal('ghAddToProfileModal');
            window.showGenHubToast('Success', 'Content Attached', `"${cTitle}" linked to "${pName}" workspace.`);
        });
    });

    const addModalCreateNewBtn = document.getElementById('ghAddModalCreateNewBtn');
    if (addModalCreateNewBtn) {
        addModalCreateNewBtn.addEventListener('click', () => {
            closeModal('ghAddToProfileModal');
            activeCardEditing = null;
            isCreatingProfile = true;
            openModal('ghGameProfileSettingsWindow');
        });
    }

    const openManifestBtn = document.getElementById('ghOpenManifestBtn');
    if (openManifestBtn) {
        openManifestBtn.addEventListener('click', () => {
            window.showGenHubToast('Info', 'Manifests Directory', 'Storage path: ~/.local/share/GenHub/manifests/');
        });
    }

    // -------------------------------------------------------------
    // TOOLS TAB: REPLAY & MAP MANAGERS
    // -------------------------------------------------------------
    const toolBtns = document.querySelectorAll('.gh-tool-item');
    const toolPanes = {
        'replay': document.getElementById('tool-pane-replay'),
        'map': document.getElementById('tool-pane-map'),
        'modbuilder': document.getElementById('tool-pane-modbuilder'),
        'genpatcher': document.getElementById('tool-pane-genpatcher')
    };

    toolBtns.forEach(btn => {
        btn.addEventListener('click', () => {
            const key = btn.getAttribute('data-tool');
            toolBtns.forEach(b => b.classList.remove('active'));
            btn.classList.add('active');

            Object.keys(toolPanes).forEach(k => {
                if (toolPanes[k]) toolPanes[k].classList.toggle('active', k === key);
            });
        });
    });

    // Map Manager Interactivity
    const mapGameSubtabs = document.querySelectorAll('[data-map-game]');
    mapGameSubtabs.forEach(btn => {
        btn.addEventListener('click', () => {
            mapGameSubtabs.forEach(b => b.classList.remove('active'));
            btn.classList.add('active');
            const g = btn.getAttribute('data-map-game');

            document.querySelectorAll('#ghMapTable tbody tr').forEach(row => {
                const rowGame = row.getAttribute('data-game') || 'zerohour';
                row.style.display = rowGame === g ? '' : 'none';
            });
        });
    });

    const mapSearchInput = document.getElementById('ghMapSearchInput');
    if (mapSearchInput) {
        mapSearchInput.addEventListener('input', (e) => {
            const q = e.target.value.toLowerCase().trim();
            document.querySelectorAll('#ghMapTable tbody tr').forEach(row => {
                const name = (row.querySelector('.map-name')?.textContent || '').toLowerCase();
                row.style.display = (!q || name.includes(q)) ? '' : 'none';
            });
        });
    }

    const mapImportSubmitBtn = document.getElementById('ghMapImportSubmitBtn');
    if (mapImportSubmitBtn) {
        mapImportSubmitBtn.addEventListener('click', () => {
            const input = document.getElementById('ghMapUrlInput');
            const url = input ? input.value.trim() : '';
            if (!url) {
                window.showGenHubToast('Warning', 'Import Map', 'Please paste a map download URL or archive link first.');
            } else {
                window.showGenHubToast('Success', 'Map Downloaded', `Successfully imported tournament map from: ${url}`);
                if (input) input.value = '';
            }
        });
    }

    const mapBrowseBtn = document.getElementById('ghMapBrowseBtn');
    if (mapBrowseBtn) {
        mapBrowseBtn.addEventListener('click', () => {
            window.showGenHubToast('Info', 'Browse Map Files', 'Select .map or .zip archive from your local filesystem.');
        });
    }

    const mapRefreshBtn = document.getElementById('ghMapRefreshBtn');
    if (mapRefreshBtn) {
        mapRefreshBtn.addEventListener('click', () => {
            window.showGenHubToast('Info', 'Maps Refreshed', 'Rescanned ~/.local/share/GenHub/Maps/ directory (6 maps loaded).');
        });
    }

    const mapFolderBtn = document.getElementById('ghMapFolderBtn');
    if (mapFolderBtn) {
        mapFolderBtn.addEventListener('click', () => {
            window.showGenHubToast('Info', 'Maps Folder', 'Opened path: ~/.local/share/GenHub/Maps/');
        });
    }

    const mapPackBtn = document.getElementById('ghMapPackBtn');
    if (mapPackBtn) {
        mapPackBtn.addEventListener('click', () => {
            window.showGenHubToast('Info', 'MapPack Manager', 'Manage bundled tournament collections and archive extraction.');
        });
    }

    document.querySelectorAll('[data-map-test]').forEach(btn => {
        btn.addEventListener('click', () => {
            const mapName = btn.getAttribute('data-map-test');
            window.showGenHubToast('Success', 'Integrity Verified', `Map "${mapName}" SHA-256 matches competitive standard.`);
        });
    });

    document.querySelectorAll('[data-map-del]').forEach(btn => {
        btn.addEventListener('click', (e) => {
            const mapName = btn.getAttribute('data-map-del');
            const row = btn.closest('tr');
            if (row) row.remove();
            window.showGenHubToast('Warning', 'Map Deleted', `Removed map "${mapName}" from local directory.`);
        });
    });

    // Replay Manager Actions
    const replaySearch = document.getElementById('ghReplaySearch');
    if (replaySearch) {
        replaySearch.addEventListener('input', (e) => {
            const q = e.target.value.toLowerCase().trim();
            document.querySelectorAll('#ghReplaysTable tbody tr').forEach(row => {
                const text = row.textContent.toLowerCase();
                row.style.display = text.includes(q) ? '' : 'none';
            });
        });
    }

    document.querySelectorAll('.gh-table-act-btn').forEach(btn => {
        btn.addEventListener('click', (e) => {
            e.stopPropagation();
            const act = btn.getAttribute('data-rep-act');
            const row = btn.closest('tr');
            const matchName = row ? (row.querySelector('strong')?.textContent || 'Match') : 'Match';

            if (act === 'watch') {
                window.showGenHubToast('Success', 'Playing Replay', `Launching Zero Hour replay player for "${matchName}".`);
            } else if (act === 'share') {
                window.showGenHubToast('Info', 'Replay Link Copied', `Upload link generated: https://genhub.online/r/151553`);
            } else if (act === 'delete') {
                row.remove();
                window.showGenHubToast('Warning', 'Replay Deleted', `Removed "${matchName}" from your local storage.`);
            }
        });
    });

    const replayImportBtn = document.getElementById('ghReplayImportBtn');
    if (replayImportBtn) {
        replayImportBtn.addEventListener('click', () => {
            const input = document.getElementById('ghReplayUrlInput');
            const val = input ? input.value.trim() : '';
            if (!val) {
                window.showGenHubToast('Warning', 'Import Replay', 'Please paste a match URL or ID first.');
            } else {
                window.showGenHubToast('Success', 'Replay Downloaded', `Successfully imported replay "${val}".`);
                if (input) input.value = '';
            }
        });
    }

    const replayRefreshBtn = document.getElementById('ghReplayRefreshBtn');
    if (replayRefreshBtn) {
        replayRefreshBtn.addEventListener('click', () => {
            window.showGenHubToast('Info', 'Replays Refreshed', 'Rescanned ~/Zero Hour Data/Replays directory.');
        });
    }

    const replayFolderBtn = document.getElementById('ghReplayFolderBtn');
    if (replayFolderBtn) {
        replayFolderBtn.addEventListener('click', () => {
            window.showGenHubToast('Info', 'Replays Folder', 'Opened path: ~/Zero Hour Data/Replays/');
        });
    }

    // ModBuilder Compile
    const mbCompileBtn = document.getElementById('ghMbCompileBtn');
    if (mbCompileBtn) {
        mbCompileBtn.addEventListener('click', () => {
            const term = document.getElementById('ghMbTerminal');
            if (term) {
                term.innerHTML = '[INFO] Compiling ModSolution INI & Art into ZeroHourMod.big...';
                setTimeout(() => {
                    term.innerHTML += '<br><span style="color:#34d399;">✓ Build Succeeded: ZeroHourMod.big (0 errors, 0 warnings).</span>';
                    window.showGenHubToast('Success', 'Mod Compiled', 'ZeroHourMod.big built successfully in 1.2s.');
                }, 500);
            }
        });
    }

    // GenPatcher Apply Fixes
    const gpBtn = document.getElementById('ghApplyGenPatcherBtn');
    if (gpBtn) {
        gpBtn.addEventListener('click', () => {
            const orig = gpBtn.textContent;
            gpBtn.textContent = 'Applying...';
            setTimeout(() => {
                gpBtn.textContent = '✓ Fixes Applied';
                setTimeout(() => gpBtn.textContent = orig, 1800);
                window.showGenHubToast('Success', 'GenPatcher Applied', 'DirectDraw fix, camera zoom, and GenTool 8.9 installed.');
            }, 600);
        });
    }

    // -------------------------------------------------------------
    // SETTINGS TAB INTERACTIVITY
    // -------------------------------------------------------------
    const settingNavBtns = document.querySelectorAll('.gh-setting-nav-btn');
    settingNavBtns.forEach(btn => {
        btn.addEventListener('click', () => {
            settingNavBtns.forEach(b => b.classList.remove('active'));
            btn.classList.add('active');

            const targetId = btn.getAttribute('data-s-target');
            const targetEl = document.getElementById(targetId);
            if (targetEl) {
                targetEl.scrollIntoView({ behavior: 'smooth', block: 'start' });
            }
        });
    });

    function applyThemeAccent(colorHex) {
        document.documentElement.style.setProperty('--accent-glow', `rgba(${parseInt(colorHex.slice(1,3),16)}, ${parseInt(colorHex.slice(3,5),16)}, ${parseInt(colorHex.slice(5,7),16)}, 0.4)`);
        document.documentElement.style.setProperty('--accent-color', colorHex);

        document.querySelectorAll('.gh-theme-swatch').forEach(sw => {
            sw.classList.toggle('active', sw.getAttribute('data-color') === colorHex);
        });
    }

    document.querySelectorAll('.gh-theme-swatch').forEach(sw => {
        sw.addEventListener('click', () => {
            const color = sw.getAttribute('data-color');
            applyThemeAccent(color);
            window.showGenHubToast('Success', 'Theme Swatch Applied', `Updated client accent color to ${color}`);
        });
    });

    document.querySelectorAll('.gh-dir-act-btn').forEach(btn => {
        btn.addEventListener('click', () => {
            const d = btn.getAttribute('data-dir');
            window.showGenHubToast('Info', 'Opened Directory', `Accessed path: ~/.local/share/GenHub/${d.replace(' ', '')}/`);
        });
    });

    document.querySelectorAll('.gh-log-btn').forEach(btn => {
        btn.addEventListener('click', () => {
            const act = btn.getAttribute('data-log-act');
            if (act === 'clear') {
                window.showGenHubToast('Warning', 'Logs Cleared', 'Emptied ~/.local/share/GenHub/logs/');
            } else if (act === 'copy-latest') {
                window.showGenHubToast('Success', 'Copied to Clipboard', 'Copied 1,480 lines of GenHub.log to clipboard.');
            } else {
                window.showGenHubToast('Info', 'Log Viewer', 'Opened latest diagnostic session output.');
            }
        });
    });

    const gcBtn = document.getElementById('ghGcCollectBtn');
    if (gcBtn) {
        gcBtn.addEventListener('click', () => {
            const memVal = document.getElementById('ghMemoryUsageVal');
            if (memVal) memVal.textContent = '38.4 MB';
            window.showGenHubToast('Success', 'Memory Cleaned', 'Garbage collection executed. Reclaimed 45.8 MB of heap memory.');
        });
    }

    // -------------------------------------------------------------
    // INFO TAB INTERACTIVITY (DefaultInfoContentProvider.cs Data)
    // -------------------------------------------------------------
    const infoNavBtns = document.querySelectorAll('.gh-info-nav-btn');
    const infoData = {
        'quickstart': {
            title: 'Quickstart Guide',
            desc: 'Getting started with GenHub.',
            cards: [
                {
                    title: 'Welcome to GenHub',
                    chip: 'Concept',
                    summary: 'Your central launcher for Command & Conquer: Generals and Zero Hour.',
                    detail: 'GenHub keeps your game, mods, custom maps, and multiplayer services organized and isolated so you can switch setups instantly without breaking your original game installation.'
                },
                {
                    title: 'Step 1: Scan for Games',
                    chip: 'How-To',
                    summary: 'Locate and link your game installation.',
                    detail: 'Navigate to the Game Profiles tab and click SCAN. GenHub automatically detects Steam, EA App, and retail disc installations.'
                },
                {
                    title: 'Step 2: Essential Downloads',
                    chip: 'Feature',
                    summary: 'Recommended community updates for modern systems.',
                    detail: 'Generals Online provides modern NAT matchmaking. TheSuperHackers Engine delivers widescreen resolution and 60+ FPS uncap.'
                },
                {
                    title: 'The Core: Manifests & CAS',
                    chip: 'Concept',
                    summary: 'How GenHub manages files and saves disk space.',
                    detail: 'Package manifests clearly list files and dependencies. Central Storage Pool (CAS) stores files once by SHA-256 hash, saving gigabytes of disk space across multiple mods.'
                }
            ]
        },
        'profiles': {
            title: 'Game Profiles',
            desc: 'Create and manage isolated game configurations.',
            cards: [
                {
                    title: 'Your Personal Sandbox',
                    chip: 'Concept',
                    summary: 'Keep your mods, maps, and game settings isolated and safe.',
                    detail: 'A profile is an independent configuration for your game. Mod files never overwrite your original game files. Profiles launch with zero extra disk space using NTFS hardlinks.'
                },
                {
                    title: 'Profile Controls Reference',
                    chip: 'How-To',
                    summary: 'Quick reference for profile card buttons.',
                    detail: 'Play launches the game. Edit Profile opens the editor. Duplicate clones the profile and all active mods. Shortcut creates a desktop icon.'
                },
                {
                    title: 'Advanced Profile Options',
                    chip: 'Feature',
                    summary: 'Custom launch arguments and troubleshooting.',
                    detail: 'Pass command-line arguments like -quickstart or -win directly to the game. Diagnostic logs are recorded in AppData.'
                }
            ]
        },
        'settings': {
            title: 'Game Settings',
            desc: 'Configure display, audio, and engine settings per profile.',
            cards: [
                {
                    title: 'Standard Audio & Video',
                    chip: 'Concept',
                    summary: 'Display and audio settings for the Generals engine (Options.ini).',
                    detail: 'Supports modern widescreen, 1440p, 4K, borderless windowed mode, 128 sound channels, and classic or right-click attack schemes.'
                },
                {
                    title: 'TheSuperHackers Engine Settings',
                    chip: 'Feature',
                    summary: 'Advanced engine capabilities.',
                    detail: 'DirectX 9 / Vulkan translation layers, high-refresh rate displays up to 240Hz, and camera pitch overrides.'
                }
            ]
        },
        'content': {
            title: 'Game Profile Content',
            desc: 'Organize and attach mods, patches, and addons to profiles.',
            cards: [
                {
                    title: 'Content Ordering and Precedence',
                    chip: 'Concept',
                    summary: 'How load priority determines which mod files take precedence.',
                    detail: 'Files at higher priority levels override identical files in lower levels without mutating either source archive.'
                }
            ]
        },
        'shortcuts': {
            title: 'Shortcuts',
            desc: 'Create quick desktop shortcuts to launch profiles directly.',
            cards: [
                {
                    title: 'Desktop Integration',
                    chip: 'How-To',
                    summary: 'Launch directly into modded setups without opening the launcher.',
                    detail: 'Desktop shortcuts invoke GenHub with the --profile-id argument, preparing the hardlink workspace and launching seamlessly.'
                }
            ]
        },
        'steam': {
            title: 'Steam Integration',
            desc: 'Track playtime and access the Steam Overlay.',
            cards: [
                {
                    title: 'Steam Broadcast & Overlay',
                    chip: 'Feature',
                    summary: 'Keep your friends updated while playing mods.',
                    detail: 'GenHub links with Steam AppID 1777510 to ensure the Steam In-Game Overlay and playtime counter work accurately across all profiles.'
                }
            ]
        },
        'local': {
            title: 'Local Content',
            desc: 'Import custom mod archives and local map folders.',
            cards: [
                {
                    title: 'Custom Content Directories',
                    chip: 'How-To',
                    summary: 'Point GenHub to existing mod folders on your drives.',
                    detail: 'GenHub scans your folders and packages them into clean local manifests ready to attach to any profile.'
                }
            ]
        },
        'tools': {
            title: 'Tools & Utilities',
            desc: 'Built-in diagnostic utilities, replay viewer, and map manager.',
            cards: [
                {
                    title: 'Map & Replay Utilities',
                    chip: 'Concept',
                    summary: 'Tournament-ready match inspection and map organization.',
                    detail: 'Inspect match APM, verify replay checksums against Generals Online matches, and drag-and-drop tournament maps.'
                }
            ]
        },
        'gofaq': {
            title: 'Generals Online FAQ',
            desc: 'Multiplayer matchmaking, ladder ranks, and connectivity.',
            cards: [
                {
                    title: 'Modern Multiplayer Lobbies',
                    chip: 'FAQ',
                    summary: 'Direct replacement for the discontinued GameSpy network.',
                    detail: 'Generals Online delivers automated NAT traversal, eliminating port forwarding and GameRanger requirements.'
                }
            ]
        },
        'gochange': {
            title: 'Generals Online Changelog',
            desc: 'Multiplayer client releases and ladder updates.',
            cards: [
                {
                    title: 'v2.1.4 Competitive Update',
                    chip: 'Changelog',
                    summary: 'Ranked ladder calibration and ping optimization.',
                    detail: 'Optimized server relay latency and fixed spectator mode desync during superweapon detonations.'
                }
            ]
        },
        'scangames': {
            title: 'Scan For Games',
            desc: 'Game detection rules and troubleshooting.',
            cards: [
                {
                    title: 'Supported Game Releases',
                    chip: 'How-To',
                    summary: 'Steam The Ultimate Collection, EA App, Origin, CD/DVD.',
                    detail: 'GenHub automatically detects registry keys and file manifests across all known official releases.'
                }
            ]
        },
        'workspace': {
            title: 'Workspace Isolation',
            desc: 'How NTFS Hardlinks keep game folders pristine.',
            cards: [
                {
                    title: 'Zero-Copy Sandboxing',
                    chip: 'Concept',
                    summary: 'Instantaneous profile switching without duplicating 10 GB of game files.',
                    detail: 'By creating NTFS hardlinks to clean game files, each mod runs in an isolated directory consuming 0 bytes of extra disk space.'
                }
            ]
        },
        'appupdates': {
            title: 'App Updates',
            desc: 'Automatic client maintenance and self-updating.',
            cards: [
                {
                    title: 'Continuous Delivery',
                    chip: 'Feature',
                    summary: 'Seamless background updates for GenHub and engine plugins.',
                    detail: 'Updates are downloaded and verified against cryptographic signatures before being staged.'
                }
            ]
        },
        'changelog': {
            title: 'Changelog',
            desc: 'GenHub launcher release history.',
            cards: [
                {
                    title: 'GenHub Alpha 3 (v0.0.3)',
                    chip: 'Changelog',
                    summary: 'Complete Avalonia XAML rewrite with CAS deduplication.',
                    detail: 'Added full ContentDetailView, MapManager DataGrid, GameProfileSettingsWindow, and enhanced multi-publisher catalog.'
                }
            ]
        }
    };

    infoNavBtns.forEach(btn => {
        btn.addEventListener('click', () => {
            const id = btn.getAttribute('data-info-id');
            infoNavBtns.forEach(b => b.classList.remove('active'));
            btn.classList.add('active');

            const section = infoData[id] || infoData['quickstart'];
            const titleEl = document.getElementById('ghInfoSectionTitle');
            const descEl = document.getElementById('ghInfoSectionDesc');
            const container = document.getElementById('ghInfoCardsContainer');

            if (titleEl) titleEl.textContent = section.title;
            if (descEl) descEl.textContent = section.desc;

            if (container) {
                container.innerHTML = section.cards.map(c => `
                    <div class="gh-info-card">
                        <div class="gh-info-card-header">
                            <h4>${c.title}</h4>
                            <span class="gh-chip">${c.chip}</span>
                        </div>
                        <p>${c.summary}</p>
                        <button class="gh-info-expand-btn">Show Details ▾</button>
                        <div class="gh-info-detailed-content">
                            ${c.detail}
                        </div>
                    </div>
                `).join('');

                container.querySelectorAll('.gh-info-expand-btn').forEach(b => {
                    b.addEventListener('click', () => {
                        const content = b.nextElementSibling;
                        if (content) {
                            const isShown = content.classList.toggle('active');
                            b.textContent = isShown ? 'Hide Details ▴' : 'Show Details ▾';
                        }
                    });
                });
            }
        });
    });

    // Wire up initial expand buttons in Info
    document.querySelectorAll('.gh-info-expand-btn').forEach(b => {
        b.addEventListener('click', () => {
            const content = b.nextElementSibling;
            if (content) {
                const isShown = content.classList.toggle('active');
                b.textContent = isShown ? 'Hide Details ▴' : 'Show Details ▾';
            }
        });
    });

})();
