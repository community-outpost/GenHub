/**
 * GenHub Native Desktop Client Simulation (Avalonia XAML Engine Parity)
 * 1:1 Flow, Modals, State Handling, Notifications, Diagnostics
 */
(function() {
    'use strict';

    // HTML Escape Helper
    function escapeHtml(str) {
        if (!str) return '';
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    // Global Toast Notification Helper
    window.showGenHubToast = function(type, title, desc) {
        if (window.isToastsMuted) return;
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
                <div class="genhub-toast-title">${escapeHtml(title)}</div>
                <div class="genhub-toast-desc">${escapeHtml(desc)}</div>
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

    // Document Escape Key Listener
    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape') {
            document.querySelectorAll('.gh-modal-backdrop.open').forEach(modal => {
                closeModal(modal.id);
            });
        }
    });
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
    let activeToolKey = 'replay';

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
            else if (paneKey === 'tools') {
                statusText.textContent = 'Tools Suite • Replay Analyzer, Map Manager & Hotkeys Editor Active';
                document.querySelectorAll('.gh-tool-item').forEach(b => {
                    b.classList.toggle('active', b.getAttribute('data-tool') === activeToolKey);
                });
                document.querySelectorAll('.gh-tool-subpane').forEach(pane => {
                    pane.classList.toggle('active', pane.id === 'tool-pane-' + activeToolKey);
                });
            }
            else if (paneKey === 'settings') statusText.textContent = 'Configuration Loaded (~/.config/GenHub/settings.json)';
            else if (paneKey === 'info') {
                statusText.textContent = 'Documentation & Frequently Asked Questions';
                const infoContent = document.getElementById('ghInfoContentArea');
                if (infoContent) infoContent.scrollTop = 0;
            }
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
    const notifTbBtn = document.getElementById('ghTbBtnBell') || document.getElementById('ghTbBtnNotifications');
    const notifFlyout = document.getElementById('ghNotificationFlyout');
    const notifBadge = document.getElementById('ghBellBadge') || document.getElementById('ghTbBellBadge') || document.getElementById('ghNotifBadge');
    const notifFlyoutBadge = document.getElementById('ghNotifFlyoutBadge');

    if (notifTbBtn && notifFlyout) {
        notifTbBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            const isActive = notifFlyout.classList.toggle('active');
            notifTbBtn.classList.toggle('active', isActive);
            if (isActive && notifBadge) {
                notifBadge.style.display = 'none';
                if (notifFlyoutBadge) notifFlyoutBadge.textContent = '0 Unread';
            }
        });

        document.addEventListener('click', (e) => {
            if (!notifFlyout.contains(e.target) && !notifTbBtn.contains(e.target)) {
                notifFlyout.classList.remove('active');
                notifTbBtn.classList.remove('active');
            }
        });
    }

    const clearNotifsBtn = document.getElementById('ghNotifClearBtn') || document.getElementById('ghClearNotifsBtn');
    if (clearNotifsBtn) {
        clearNotifsBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            const list = document.getElementById('ghNotifList');
            if (list) {
                list.innerHTML = `
                    <div style="padding: 28px 16px; text-align: center; color: #64748b; font-size: 12.5px;">
                        No pending notifications
                    </div>
                `;
            }
            if (notifBadge) notifBadge.style.display = 'none';
            if (notifFlyoutBadge) notifFlyoutBadge.textContent = '0 Unread';
            window.showGenHubToast('Info', 'Notifications Cleared', 'All feed alerts have been marked as read.');
        });
    }

    const muteNotifsBtn = document.getElementById('ghNotifMuteBtn');
    if (muteNotifsBtn) {
        let isMuted = false;
        muteNotifsBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            isMuted = !isMuted;
            muteNotifsBtn.style.color = isMuted ? '#ef4444' : '#94a3b8';
            window.showGenHubToast('Info', isMuted ? 'Notifications Muted' : 'Notifications Unmuted', isMuted ? 'Toast popups silenced for session.' : 'Toast popups enabled.');
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

                if (typeof selectWinTab === 'function') selectWinTab(1);
                if (typeof selectSubCategory === 'function') selectSubCategory('identity');
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

                const cloneLaunch = clone.querySelector('.gh-launch-btn');
                if (cloneLaunch) {
                    cloneLaunch.classList.remove('running');
                    cloneLaunch.style.background = '';
                    const launchText = cloneLaunch.querySelector('.launch-text') || cloneLaunch;
                    if (launchText) launchText.textContent = 'LAUNCH';
                }
                
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
        const count = document.querySelectorAll('#ghProfilesList .gh-profile-card:not(.add-card)').length;
        const countEl = document.getElementById('ghProfilesCount');
        if (countEl) countEl.textContent = `Loaded ${count} profiles`;
    }

    // GameProfileSettingsWindow Sub-navigation (Content, Profile Settings, Game Settings)
    const winNavBtns = document.querySelectorAll('.gh-win-nav-btn');
    const winPanels = [
        document.getElementById('ghWinTabContent'),
        document.getElementById('ghWinTabGeneral'),
        document.getElementById('ghWinTabGame')
    ];

    function selectWinTab(idx) {
        winNavBtns.forEach((b, bIdx) => {
            b.classList.toggle('active', bIdx === idx);
            b.setAttribute('aria-selected', bIdx === idx ? 'true' : 'false');
        });
        winPanels.forEach((p, pIdx) => {
            if (p) p.classList.toggle('active', pIdx === idx);
        });
    }

    winNavBtns.forEach((btn, idx) => {
        btn.addEventListener('click', () => selectWinTab(idx));
    });

    // General Settings Sub-sidebar (Identity, Appearance, Launch, Theme)
    const subNavBtns = document.querySelectorAll('.gh-sub-nav-btn');
    const subPanels = {
        'identity': document.getElementById('ghSubIdentity'),
        'appearance': document.getElementById('ghSubAppearance'),
        'launch': document.getElementById('ghSubLaunch'),
        'theme': document.getElementById('ghSubTheme')
    };

    function selectSubCategory(cat) {
        subNavBtns.forEach(b => {
            b.classList.toggle('active', b.getAttribute('data-sub-category') === cat);
        });
        Object.keys(subPanels).forEach(k => {
            if (subPanels[k]) subPanels[k].classList.toggle('active', k === cat);
        });
    }

    subNavBtns.forEach(btn => {
        btn.addEventListener('click', () => {
            const cat = btn.getAttribute('data-sub-category');
            selectSubCategory(cat);
        });
    });

    // Add New Profile Card -> Opens GameProfileSettingsWindow at Profile Settings Identity
    const addProfileBtn = document.getElementById('ghAddNewProfileBtn');
    if (addProfileBtn) {
        addProfileBtn.addEventListener('click', () => {
            activeCardEditing = null;
            isCreatingProfile = true;
            const titleEl = document.getElementById('ghWinProfileTitle');
            const nameInput = document.getElementById('ghProfileNameInput');
            if (titleEl) titleEl.textContent = 'New Profile';
            if (nameInput) nameInput.value = 'Custom Profile';
            selectWinTab(1);
            selectSubCategory('identity');
            openModal('ghGameProfileSettingsWindow');
        });
    }

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

    // Window Fullscreen / Maximize Toggle (Constrained cleanly inside App Window)
    const fsBtn = document.getElementById('ghWinFullscreenBtn');
    if (fsBtn) {
        fsBtn.addEventListener('click', () => {
            const win = document.querySelector('.gh-profile-settings-window');
            if (win) {
                win.style.width = '';
                win.style.maxWidth = '';
                win.style.height = '';
                win.style.maxHeight = '';
                win.classList.toggle('fullscreen-max');
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
                            <svg viewBox="0 0 24 24" width="13" height="13" fill="currentColor"><use href="#gh-icon-edit"></use></svg>
                        </button>
                        <button class="gh-card-act-btn clone-btn" title="Duplicate Profile">
                            <svg viewBox="0 0 24 24" width="13" height="13" fill="currentColor"><use href="#gh-icon-clone"></use></svg>
                        </button>
                        <button class="gh-card-act-btn shortcut-btn" title="Create Desktop Shortcut">
                            <svg viewBox="0 0 24 24" width="13" height="13" fill="currentColor"><use href="#gh-icon-shortcut"></use></svg>
                        </button>
                        <button class="gh-card-act-btn delete-btn" title="Delete Profile">
                            <svg viewBox="0 0 24 24" width="13" height="13" fill="currentColor"><use href="#gh-icon-delete"></use></svg>
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
                                <div class="gh-card-title">${escapeHtml(name)}</div>
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
        'aodmaps': { title: 'AODMaps', desc: 'Curated Art of Defense tower defense maps, survival scenarios, and cooperative challenges.' },
        'github': { title: 'GitHub Releases', desc: 'Open source builds, experimental test branches, and engine source code.' },
        'moddb': { title: 'ModDB Mirror', desc: 'Community mods, total conversions, maps, addons, and direct URL search.' }
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
            const contentName = btn.getAttribute('data-view-content') || card?.getAttribute('data-title') || 'Item';
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
            const subEl = document.getElementById('ghAddModalSubtitle');
            if (subEl) subEl.textContent = `${contentName} — Zero Hour`;
            openModal('ghAddToProfileModal');
        });
    });

    document.querySelectorAll('.gh-sel-card:not(.create-new)').forEach(card => {
        card.addEventListener('click', () => {
            const pName = card.getAttribute('data-target-profile') || card.getAttribute('data-profile-name') || 'Profile';
            const rawSub = document.getElementById('ghAddModalSubtitle')?.textContent || 'Item';
            const cTitle = rawSub.split(' — ')[0];
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
            const winTitle = document.getElementById('ghWinProfileTitle');
            if (winTitle) winTitle.textContent = 'New Profile';
            const nameInput = document.getElementById('ghProfileNameInput');
            if (nameInput) nameInput.value = 'Custom Profile';
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
        'hotkeys': document.getElementById('tool-pane-hotkeys'),
        'genpatcher': document.getElementById('tool-pane-genpatcher')
    };

    toolBtns.forEach(btn => {
        btn.addEventListener('click', () => {
            const key = btn.getAttribute('data-tool');
            activeToolKey = key;
            toolBtns.forEach(b => b.classList.remove('active'));
            btn.classList.add('active');

            Object.keys(toolPanes).forEach(k => {
                if (toolPanes[k]) toolPanes[k].classList.toggle('active', k === key);
            });
        });
    });

    // Map Manager Interactivity - Unified Filter
    function filterMaps() {
        const activeTab = document.querySelector('[data-map-game].active');
        const selectedGame = activeTab ? activeTab.getAttribute('data-map-game') : 'zerohour';
        const q = (document.getElementById('ghMapSearchInput')?.value || '').toLowerCase().trim();
        let count = 0;

        document.querySelectorAll('#ghMapTable tbody tr').forEach(row => {
            const rowGame = row.getAttribute('data-game') || 'zerohour';
            const name = (row.querySelector('.map-name')?.textContent || '').toLowerCase();
            const matchesGame = !selectedGame || rowGame === selectedGame;
            const matchesQuery = !q || name.includes(q);
            const show = matchesGame && matchesQuery;
            row.style.display = show ? '' : 'none';
            if (show) count++;
        });

        const statusBar = document.getElementById('ghMapStatusBar');
        if (statusBar) {
            const gameLabel = selectedGame === 'generals' ? 'Generals' : 'Zero Hour';
            statusBar.textContent = `${count} Maps indexed (${gameLabel}) • 0 Selected`;
        }
    }

    const mapGameSubtabs = document.querySelectorAll('[data-map-game]');
    mapGameSubtabs.forEach(btn => {
        btn.addEventListener('click', () => {
            mapGameSubtabs.forEach(b => b.classList.remove('active'));
            btn.classList.add('active');
            filterMaps();
        });
    });

    const mapSearchInput = document.getElementById('ghMapSearchInput');
    if (mapSearchInput) {
        mapSearchInput.addEventListener('input', () => filterMaps());
    }

    // Initialize Map filtering on load
    filterMaps();

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

    // Replay Manager Actions (PR #422 CRC Catalog & Compatibility Integration)
    const replaySearch = document.getElementById("ghReplaySearch");
    if (replaySearch) {
        replaySearch.addEventListener("input", (e) => {
            const q = e.target.value.toLowerCase().trim();
            document.querySelectorAll("#ghReplaysTable tbody tr").forEach(row => {
                const text = row.textContent.toLowerCase();
                row.style.display = text.includes(q) ? "" : "none";
            });
        });
    }

    function updateReplaySelectedCount() {
        const countSpan = document.getElementById("ghReplaySelectedCount");
        if (!countSpan) return;
        const selected = document.querySelectorAll("#ghReplaysTable tbody tr.selected").length;
        const total = document.querySelectorAll("#ghReplaysTable tbody tr").length;
        countSpan.innerHTML = `<strong>${selected || (total > 0 ? 1 : 0)}</strong> Selected`;
    }

    // Row selection toggle
    document.querySelectorAll("#ghReplaysTable tbody tr").forEach(row => {
        row.addEventListener("click", (e) => {
            if (e.target.closest("button") || e.target.closest("input")) return;
            row.classList.toggle("selected");
            updateReplaySelectedCount();
        });
    });

    // Replay row actions (Launch, Setup, Profile, Folder, Delete)
    document.getElementById("ghReplaysTable")?.addEventListener("click", (e) => {
        const actBtn = e.target.closest("[data-rep-act]");
        if (!actBtn) return;
        e.stopPropagation();

        const act = actBtn.getAttribute("data-rep-act");
        const row = actBtn.closest("tr");
        const matchName = row ? (row.querySelector("strong")?.textContent || "Replay") : "Replay";
        const client = row?.getAttribute("data-rep-client") || "Zero Hour Client";
        const exeCrc = row?.getAttribute("data-rep-exe") || "0x8F3A2B1C";

        if (act === "launch") {
            window.showGenHubToast("Success", "Launching Replay", `Matched ${client} (${exeCrc}). Starting desync-free playback for "${matchName}".`);
        } else if (act === "setup") {
            window.showGenHubToast("Info", "Auto-Configuring Profile", `Creating dedicated profile for ${client} via CRC catalog mapping.`);
        } else if (act === "profile") {
            window.showGenHubToast("Info", "Replay Profile Options", `Opening client selector dialog for "${matchName}".`);
        } else if (act === "folder") {
            window.showGenHubToast("Info", "Opening Explorer", `Revealed replay file in ~/.local/share/GenHub/Replays/`);
        } else if (act === "delete") {
            row.remove();
            updateReplaySelectedCount();
            window.showGenHubToast("Warning", "Replay Deleted", `Removed "${matchName}" from local storage.`);
        }
    });

    // Replay Action Bar Controls
    document.getElementById("ghReplayDeleteSelectedBtn")?.addEventListener("click", () => {
        const selected = document.querySelectorAll("#ghReplaysTable tbody tr.selected");
        if (selected.length > 0) {
            const count = selected.length;
            selected.forEach(r => r.remove());
            updateReplaySelectedCount();
            window.showGenHubToast("Warning", "Replays Deleted", `Deleted ${count} selected replay file(s).`);
        } else {
            const firstRow = document.querySelector("#ghReplaysTable tbody tr");
            if (firstRow) {
                firstRow.remove();
                updateReplaySelectedCount();
                window.showGenHubToast("Warning", "Replay Deleted", "Deleted 1 selected replay.");
            }
        }
    });

    document.getElementById("ghReplayUncompressBtn")?.addEventListener("click", () => {
        window.showGenHubToast("Success", "Uncompress Archives", "Extracted .rep recordings from selected archives into replay directory.");
    });

    document.getElementById("ghReplayZipBtn")?.addEventListener("click", () => {
        const zipName = document.getElementById("ghReplayZipName")?.value || "Replays.zip";
        window.showGenHubToast("Success", "ZIP Export Complete", `Archived selected replays into "${zipName}".`);
    });

    document.getElementById("ghReplayUploadShareBtn")?.addEventListener("click", () => {
        window.showGenHubToast("Success", "Cloud Link Ready", "Uploaded replay to cloud: https://genhub.online/r/8f3a2b1c");
    });

    document.getElementById("ghReplayHistoryToggleBtn")?.addEventListener("click", () => {
        window.showGenHubToast("Info", "Upload History", "Viewing cloud upload history (3 shared replays active).");
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

    // GenHotkeys Interactivity (PR #451)
    const factionTabs = document.querySelectorAll("#ghHotkeyFactionTabs .gh-faction-btn");
    const generalSelect = document.getElementById("ghHotkeyGeneralSelect");
    const catPills = document.querySelectorAll("#ghHotkeyCategoryPills .gh-cat-pill");
    const hotkeyCards = document.querySelectorAll("#ghHotkeyGrid .gh-action-card");
    const presetSelect = document.getElementById("ghHotkeyPresetSelect");
    const profileSelect = document.getElementById("ghHotkeyProfileSelect");
    const cornerSelect = document.getElementById("ghHotkeyCornerSelect");
    const overlayCheck = document.getElementById("ghHotkeyOverlayCheck");
    const createAddonBtn = document.getElementById("ghHotkeyCreateAddonBtn");
    let activeFaction = "usa";
    let activeCat = "all";

    const factionGenerals = {
        "usa": [
            { val: "all", label: "All USA Generals" },
            { val: "laser", label: "Laser General (Townes)" },
            { val: "airforce", label: "Air Force General (Granger)" },
            { val: "superweapon", label: "Superweapon (Alexander)" }
        ],
        "china": [
            { val: "all", label: "All China Generals" },
            { val: "tank", label: "Tank General (Kwai)" },
            { val: "infantry", label: "Infantry General (Fai)" },
            { val: "nuke", label: "Nuke General (Tao)" }
        ],
        "gla": [
            { val: "all", label: "All GLA Generals" },
            { val: "toxin", label: "Toxin General (Thrax)" },
            { val: "demo", label: "Demolition General (Juhziz)" },
            { val: "stealth", label: "Stealth General (Kassad)" }
        ]
    };

    function filterHotkeyCards() {
        hotkeyCards.forEach(card => {
            const cFaction = card.getAttribute("data-faction");
            const cCat = card.getAttribute("data-cat");
            const matchFaction = cFaction === activeFaction;
            const matchCat = activeCat === "all" || cCat === activeCat;
            card.style.display = (matchFaction && matchCat) ? "flex" : "none";
        });
    }

    factionTabs.forEach(btn => {
        btn.addEventListener("click", () => {
            factionTabs.forEach(b => b.classList.remove("active"));
            btn.classList.add("active");
            activeFaction = btn.getAttribute("data-faction") || "usa";

            if (generalSelect && factionGenerals[activeFaction]) {
                generalSelect.innerHTML = "";
                factionGenerals[activeFaction].forEach(g => {
                    const opt = document.createElement("option");
                    opt.value = g.val;
                    opt.textContent = g.label;
                    generalSelect.appendChild(opt);
                });
            }

            filterHotkeyCards();
        });
    });

    catPills.forEach(pill => {
        pill.addEventListener("click", () => {
            catPills.forEach(p => p.classList.remove("active"));
            pill.classList.add("active");
            activeCat = pill.getAttribute("data-cat") || "all";
            filterHotkeyCards();
        });
    });

    if (generalSelect) {
        generalSelect.addEventListener("change", () => {
            const label = generalSelect.options[generalSelect.selectedIndex]?.text || "General";
            window.showGenHubToast("GenHotkeys", "General Profile", "Switched variant: " + label);
        });
    }

    if (profileSelect) {
        profileSelect.addEventListener("change", () => {
            const label = profileSelect.options[profileSelect.selectedIndex]?.text || "Profile";
            window.showGenHubToast("GenHotkeys", "Profile Switched", "Active configuration: " + label);
        });
    }

    const hotkeyPresets = {
        "grid": {
            // USA
            "USADozer": "Q", "USAColdFusionReactor": "W", "USABarracks": "E", "USAWarFactory": "R",
            "USAPatriot": "A", "USAAirfield": "S", "USARanger": "R", "USAHumvee": "H",
            "USACrusaderTank": "C", "USAComanche": "C", "USARaptor": "P", "USAParticleCannon": "U",
            // China
            "PRCDozer": "Q", "PRCNuclearReactor": "W", "PRCBarracks": "E", "PRCWarFactory": "R",
            "PRCBunker": "A", "PRCGattlingCannon": "S", "PRCRedGuard": "R", "PRCTankHunter": "T",
            "PRCBattlemaster": "B", "PRCGattlingTank": "G", "PRCOverlordTank": "O", "PRCMIG": "M",
            // GLA
            "GLACommandCenter": "Q", "GLABarracks": "E", "GLAArmsDealer": "R", "GLABlackMarket": "B",
            "GLADemoTrap": "D", "GLACamoNetting": "N", "GLAJarmenKell": "J", "GLAAngryMob": "M",
            "GLAScorpionTank": "S", "GLARocketBuggy": "B", "GLABombTruck": "T", "GLABattleBus": "U"
        },
        "retail": {
            // USA
            "USADozer": "D", "USAColdFusionReactor": "R", "USABarracks": "B", "USAWarFactory": "W",
            "USAPatriot": "P", "USAAirfield": "A", "USARanger": "R", "USAHumvee": "H",
            "USACrusaderTank": "C", "USAComanche": "C", "USARaptor": "R", "USAParticleCannon": "P",
            // China
            "PRCDozer": "D", "PRCNuclearReactor": "R", "PRCBarracks": "B", "PRCWarFactory": "W",
            "PRCBunker": "U", "PRCGattlingCannon": "G", "PRCRedGuard": "R", "PRCTankHunter": "T",
            "PRCBattlemaster": "B", "PRCGattlingTank": "K", "PRCOverlordTank": "O", "PRCMIG": "M",
            // GLA
            "GLACommandCenter": "C", "GLABarracks": "B", "GLAArmsDealer": "A", "GLABlackMarket": "M",
            "GLADemoTrap": "T", "GLACamoNetting": "N", "GLAJarmenKell": "J", "GLAAngryMob": "M",
            "GLAScorpionTank": "S", "GLARocketBuggy": "R", "GLABombTruck": "T", "GLABattleBus": "B"
        },
        "wasd": {
            // USA
            "USADozer": "Q", "USAColdFusionReactor": "E", "USABarracks": "R", "USAWarFactory": "T",
            "USAPatriot": "F", "USAAirfield": "G", "USARanger": "Z", "USAHumvee": "X",
            "USACrusaderTank": "C", "USAComanche": "V", "USARaptor": "B", "USAParticleCannon": "Y",
            // China
            "PRCDozer": "Q", "PRCNuclearReactor": "E", "PRCBarracks": "R", "PRCWarFactory": "T",
            "PRCBunker": "F", "PRCGattlingCannon": "G", "PRCRedGuard": "Z", "PRCTankHunter": "X",
            "PRCBattlemaster": "C", "PRCGattlingTank": "V", "PRCOverlordTank": "B", "PRCMIG": "N",
            // GLA
            "GLACommandCenter": "Q", "GLABarracks": "E", "GLAArmsDealer": "R", "GLABlackMarket": "T",
            "GLADemoTrap": "F", "GLACamoNetting": "G", "GLAJarmenKell": "Z", "GLAAngryMob": "X",
            "GLAScorpionTank": "C", "GLARocketBuggy": "V", "GLABombTruck": "B", "GLABattleBus": "N"
        }
    };

    if (presetSelect) {
        presetSelect.addEventListener("change", () => {
            const p = presetSelect.value;
            const presetMap = hotkeyPresets[p];
            if (presetMap) {
                hotkeyCards.forEach(card => {
                    const id = card.getAttribute("data-id");
                    if (id && presetMap[id]) {
                        const stamp = card.querySelector(".gh-hotkey-stamp");
                        const badge = card.querySelector(".gh-key-badge");
                        if (stamp) stamp.textContent = presetMap[id];
                        if (badge) badge.textContent = presetMap[id];
                    }
                });
                const label = presetSelect.options[presetSelect.selectedIndex]?.text || p;
                window.showGenHubToast("GenHotkeys", "Preset Applied", "Loaded " + label + " key mapping preset.");
            }
        });
    }

    if (cornerSelect) {
        cornerSelect.addEventListener("change", () => {
            const corner = cornerSelect.value;
            const stamps = document.querySelectorAll(".gh-hotkey-stamp");
            stamps.forEach(s => {
                s.style.top = (corner === "tr" || corner === "tl") ? "3px" : "auto";
                s.style.bottom = (corner === "br" || corner === "bl") ? "3px" : "auto";
                s.style.left = (corner === "tl" || corner === "bl") ? "4px" : "auto";
                s.style.right = (corner === "tr" || corner === "br") ? "4px" : "auto";
            });
            const label = cornerSelect.options[cornerSelect.selectedIndex]?.text || corner;
            window.showGenHubToast("GenHotkeys", "Stamp Position", "Badges aligned to " + label + ".");
        });
    }

    if (overlayCheck) {
        overlayCheck.addEventListener("change", () => {
            const stamps = document.querySelectorAll(".gh-hotkey-stamp");
            stamps.forEach(s => {
                s.style.display = overlayCheck.checked ? "block" : "none";
            });
            window.showGenHubToast("GenHotkeys", "Cameo Badges", overlayCheck.checked ? "Enabled hotkey overlay badges." : "Disabled hotkey overlay badges.");
        });
    }

    hotkeyCards.forEach(card => {
        card.addEventListener("click", () => {
            hotkeyCards.forEach(c => c.classList.remove("selected"));
            card.classList.add("selected");
            const title = card.querySelector(".gh-action-title")?.textContent || "Action";
            const stamp = card.querySelector(".gh-hotkey-stamp")?.textContent || "";
            window.showGenHubToast("Hotkeys Editor", "Command Selected", title + " [" + stamp + "] ready for binding.");
        });
    });

    if (createAddonBtn) {
        createAddonBtn.addEventListener("click", () => {
            createAddonBtn.disabled = true;
            createAddonBtn.innerHTML = "<span>Compiling...</span>";
            setTimeout(() => {
                createAddonBtn.disabled = false;
                createAddonBtn.innerHTML = "<svg viewBox=\"0 0 24 24\" width=\"12\" height=\"12\" fill=\"currentColor\"><path d=\"M19,9H15V3H9V9H5L12,16L19,9M5,18V20H19V18H5Z\"/></svg> <span>Create Addon</span>";
                window.showGenHubToast("GenHotkeys", "Addon Archive Compiled", "Generated !Hotkeys_Pro_ZH.big with stamped cameos. Registered into CAS.");
            }, 600);
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
    // SETTINGS TAB INTERACTIVITY (Avalonia Expander & Sidebar Nav)
    // -------------------------------------------------------------
    const settingNavBtns = document.querySelectorAll('.gh-setting-nav-btn');
    const settingsContent = document.getElementById('ghSettingsScrollContainer') || document.querySelector('.gh-settings-content');

    // Accordion toggle on headers
    document.querySelectorAll('.gh-expander-header').forEach(header => {
        header.addEventListener('click', (e) => {
            e.preventDefault();
            const expander = header.closest('.gh-settings-expander');
            if (expander) {
                const isOpen = expander.classList.toggle('open');
                header.setAttribute('aria-expanded', isOpen ? 'true' : 'false');
            }
        });
    });

    // Expand All / Collapse All buttons
    const expandAllBtn = document.getElementById('ghExpandAllSettingsBtn');
    const collapseAllBtn = document.getElementById('ghCollapseAllSettingsBtn');
    if (expandAllBtn) {
        expandAllBtn.addEventListener('click', (e) => {
            e.preventDefault();
            document.querySelectorAll('.gh-settings-expander').forEach(el => {
                el.classList.add('open');
                const btn = el.querySelector('.gh-expander-header');
                if (btn) btn.setAttribute('aria-expanded', 'true');
            });
        });
    }
    if (collapseAllBtn) {
        collapseAllBtn.addEventListener('click', (e) => {
            e.preventDefault();
            document.querySelectorAll('.gh-settings-expander').forEach(el => {
                el.classList.remove('open');
                const btn = el.querySelector('.gh-expander-header');
                if (btn) btn.setAttribute('aria-expanded', 'false');
            });
        });
    }

    // Sidebar navigation jumps to and opens the target expander
    settingNavBtns.forEach(btn => {
        btn.addEventListener('click', (e) => {
            e.preventDefault();
            e.stopPropagation();

            settingNavBtns.forEach(b => b.classList.remove('active'));
            btn.classList.add('active');

            const targetId = btn.getAttribute('data-s-target');
            const targetEl = document.getElementById(targetId);
            if (targetEl && settingsContent) {
                targetEl.classList.add('open');
                const targetTop = targetEl.offsetTop - settingsContent.offsetTop;
                settingsContent.scrollTo({
                    top: Math.max(0, targetTop - 12),
                    behavior: 'smooth'
                });
            }
        });
    });

    function applyThemeAccent(colorHex) {
        if (!colorHex) return;
        const r = parseInt(colorHex.slice(1,3), 16) || 139;
        const g = parseInt(colorHex.slice(3,5), 16) || 92;
        const b = parseInt(colorHex.slice(5,7), 16) || 246;

        const rDark = Math.max(0, Math.round(r * 0.82));
        const gDark = Math.max(0, Math.round(g * 0.82));
        const bDark = Math.max(0, Math.round(b * 0.82));
        const colorHover = `rgb(${rDark}, ${gDark}, ${bDark})`;

        const glow = `rgba(${r}, ${g}, ${b}, 0.45)`;
        const light = `rgba(${r}, ${g}, ${b}, 0.18)`;
        const border = `rgba(${r}, ${g}, ${b}, 0.5)`;

        const appEl = document.querySelector('.genhub-app-window');
        const targets = [document.documentElement, document.body, appEl].filter(Boolean);

        targets.forEach(el => {
            el.style.setProperty('--gh-accent', colorHex);
            el.style.setProperty('--gh-accent-hover', colorHover);
            el.style.setProperty('--gh-accent-glow', glow);
            el.style.setProperty('--gh-accent-light', light);
            el.style.setProperty('--gh-accent-border', border);
            el.style.setProperty('--accent-glow', glow);
            el.style.setProperty('--accent-color', colorHex);
        });

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
            "zh_problems_game": {
            "id": "zh_problems_game",
            "title": "Problems with the Game",
            "desc": "Common startup, crash, and graphic issues for C&C Generals Zero Hour and their verified fixes.",
            "cards": [
                {
                    "title": "My game crashes on start-up",
                    "content": "Generals crashes immediately upon launch or produces a DirectX 8.1 / Technical Difficulties error.",
                    "type": "Crash Fix",
                    "detailed": "**Cause:** Missing or invalid Options.ini configuration file, incorrect display resolution, or missing legacy DirectX 9 runtimes.\n\n**Solution:**\n1. GenHub automatically generates a valid Options.ini in your profile sandbox.\n2. Open **Game Profiles** -> Select your profile -> Click **Settings** (Gear icon).\n3. Set your native screen resolution (e.g. 1920x1080, 2560x1440).\n4. Ensure **Borderless Window Hook** is enabled under profile addons.\n5. Alternatively, run GenPatcher from the **Tools** tab to automatically restore corrupt base game installations."
                },
                {
                    "title": "My game crashes after 10 to 30 minutes of gameplay",
                    "content": "The game randomly crashes mid-battle or during large skirmishes without any warning dialog.",
                    "type": "Crash Fix",
                    "detailed": "**Cause:** The 32-bit SAGE engine exceeds its 2GB virtual memory address space limit when loading high-resolution assets or mods.\n\n**Solution:**\n1. GenHub's **GeneralsGameCode Engine Patch** enables the **Large Address Aware (LAA)** flag, granting access up to 4GB RAM.\n2. Install **GenTool 8.9** from the Downloads tab to fix audio buffer leaks.\n3. Lower dynamic particle effects and disable 3D shadows in high-unit count matches."
                },
                {
                    "title": "My game crashes after using Alt + Tab",
                    "content": "Minimizing or Alt-Tabbing causes a frozen screen or Direct3D surface loss error.",
                    "type": "DirectX Fix",
                    "detailed": "**Cause:** Legacy DirectX 8 does not support dynamic device loss recovery when leaving exclusive fullscreen.\n\n**Solution:**\n* Enable **Borderless Window Hook** in your profile settings.\n* Borderless mode allows instant Alt-Tabbing across dual monitors with full cursor locking and zero crash risk."
                },
                {
                    "title": "My base blows up within 30 seconds",
                    "content": "All structures explode and defeat screen appears immediately after starting a match.",
                    "type": "Copy Protection",
                    "detailed": "**Cause:** Built-in copy protection desynchronization triggered by duplicate CD keys on LAN or corrupt registry serial entries.\n\n**Solution:**\n1. Open **Tools** -> Run **GenPatcher** to regenerate and clean your registry serial hashes.\n2. Ensure all players on LAN/matchmaking have unique installation keys."
                },
                {
                    "title": "In-game edge-based mouse scrolling is not working",
                    "content": "Moving the mouse cursor to the edges of the monitor does not pan the battlefield view.",
                    "type": "Input Fix",
                    "detailed": "**Cause:** Windows 10/11 high-DPI display scaling interferes with standard cursor border coordinates.\n\n**Solution:**\n1. In GenHub, navigate to Profile Settings -> Compatibility.\n2. Enable **Disable High DPI Scaling Override**.\n3. Or enable GenTool mouse clipping (Scroll3D=1)."
                },
                {
                    "title": "The game feels slow and sluggish",
                    "content": "Frame rates stutter or match speed lags even on modern high-end gaming CPUs.",
                    "type": "Performance",
                    "detailed": "**Cause:** Modern multi-core CPUs downclocking or scheduling threads incorrectly across E-cores / P-cores.\n\n**Solution:**\n1. Lock game process affinity to 2 physical CPU cores in GenHub Profile Settings.\n2. Ensure GenTool is enabled with dynamic frame timing unlocked (FPS=60)."
                }
            ]
        },
        "zh_problems_multiplayer": {
            "id": "zh_problems_multiplayer",
            "title": "Problems with Multiplayer",
            "desc": "Resolving network connectivity, NAT traversal, mismatch errors, and ladder synchronization.",
            "cards": [
                {
                    "title": "Unable to establish connection to other players",
                    "content": "Connection times out when attempting to join a lobby or direct connect match.",
                    "type": "Network Fix",
                    "detailed": "**Cause:** Original GameSpy master servers were shut down in 2014, and legacy direct peer-to-peer connections require strict port forwarding.\n\n**Solution:**\n1. Use the integrated **Generals Online** client included with GenHub.\n2. Generals Online automatically routes traffic through global low-latency TURN/relay servers, bypassing symmetric NAT and carrier-grade NAT (CGNAT) without manual router configuration."
                },
                {
                    "title": "Mismatch Error (Game Desync)",
                    "content": "A 'Mismatch has occurred' popup appears and the game immediately halts for all players.",
                    "type": "Desync Fix",
                    "detailed": "**Cause:** INI file mismatches, differing mod builds, or modified .big files between players.\n\n**Solution:**\n1. Always launch games from the exact same GenHub profile version.\n2. GenHub uses cryptographic SHA-256 manifest verification on profile creation to ensure byte-level synchronization with your opponents."
                },
                {
                    "title": "Direct Connect / LAN Connection Failed",
                    "content": "Players on the same local network or VPN cannot see each other in the LAN lobby.",
                    "type": "Network Fix",
                    "detailed": "**Cause:** Windows binds the game network socket to an inactive virtual network adapter (such as VMware or Docker).\n\n**Solution:**\n* In Options.ini, set IPAddress = &lt;your_local_ip&gt; to force Generals to listen on your primary LAN adapter."
                },
                {
                    "title": "Port Forwarding and Firewall Setup",
                    "content": "Manual port forwarding configuration for direct connection hosting.",
                    "type": "Firewall",
                    "detailed": "If hosting without Generals Online relays, open the following UDP ports in your router:\n* **UDP 8086-8088:** Gameplay simulation data\n* **UDP 27900:** Heartbeat and lobby query\n* **UDP 29900-29901:** Match stats and voice relay"
                }
            ]
        },
        "zh_general_faq": {
            "id": "zh_general_faq",
            "title": "Zero Hour FAQ",
            "desc": "Frequently asked questions regarding game configuration, widescreen resolutions, and anti-cheat.",
            "cards": [
                {
                    "title": "How do I play Zero Hour in modern widescreen (1440p / 4K)?",
                    "content": "Standard retail Zero Hour only supports 4:3 resolutions (800x600, 1024x768).",
                    "type": "Display Guide",
                    "detailed": "**Enabling Widescreen:**\n1. In GenHub, select your profile and open Settings.\n2. Choose **2560x1440** or **3840x2160** from the Resolution dropdown.\n3. GenHub applies the widescreen camera aspect ratio patch via GenTool so graphics scale without visual distortion."
                },
                {
                    "title": "What is GenTool and why is it mandatory for competitive play?",
                    "content": "Understanding GenTool features, anti-cheat hashes, and ladder verification.",
                    "type": "Tooling",
                    "detailed": "**GenTool Features:**\n* Anti-cheat memory scanner verifying file integrity.\n* Dynamic widescreen camera zoom fix.\n* Spectator mode tools and match upload for community leaderboards.\n* Eliminates input lag and mouse cursor jitter on modern monitors."
                },
                {
                    "title": "Can I play Zero Hour without Steam or EA App running?",
                    "content": "Running standalone or retail CD installations with GenHub.",
                    "type": "Compatibility",
                    "detailed": "Yes! GenHub supports standalone retail CD copies, First Decade installations, The Ultimate Collection, and modern digital editions. Once your base files are linked, GenHub launches isolated sandboxes directly."
                }
            ]
        },
        "quickstart": {
        "id": "quickstart",
        "title": "Quickstart Guide",
        "desc": "Getting started with GenHub.",
        "cards": [
            {
                "title": "Welcome to GenHub",
                "content": "Your central launcher for Command & Conquer: Generals and Zero Hour.",
                "type": "Concept",
                "detailed": "**What is GenHub?**\n                    GenHub is a modern launcher and manager for **Command & Conquer: Generals** and **Zero Hour**. It keeps your game, mods, custom maps, and multiplayer services organized and isolated so you can switch setups instantly without breaking your original game installation.\n\n                    **Platform Overview:**\n                    *   **Game Profiles:** Your primary hub. Scan for your game installation, set up mod configurations, and launch the game.\n                    *   **Downloads:** Direct, one-click downloads for community patches, multiplayer services, and community mods.\n                    *   **Tools:** Built-in managers for inspecting replays and organizing custom maps."
            },
            {
                "title": "Step 1: Scan for Games",
                "content": "Locate and link your game installation.",
                "type": "HowTo",
                "detailed": "**Detecting Your Game:**\n                    GenHub connects to your existing game files before launching profiles.\n\n                    1.  Navigate to the **Game Profiles** tab.\n                    2.  Click the **SCAN** button in the toolbar.\n                    3.  GenHub automatically searches standard Steam, EA App, Origin, and CD install directories.\n\n                    *Once detected, you can create and launch profiles based on this installation.*"
            },
            {
                "title": "Step 2: Essential Downloads",
                "content": "Recommended community updates for modern systems.",
                "type": "Feature",
                "detailed": "**Recommended Community Additions:**\n                    Visit the **Downloads** tab to get recommended updates for modern hardware and online play:\n\n                    *   **Generals Online:** Modern online multiplayer lobby and matchmaking, replacing the discontinued GameSpy service.\n                    *   **TheSuperHackers Engine:** Active community engine updates offering widescreen support, high-DPI scaling, and crash fixes.\n                    *   **Community Patches:** Game balance, memory enhancements, and stability fixes."
            },
            {
                "title": "Step 3: Add Local Content",
                "content": "Import your existing mods and maps.",
                "type": "HowTo",
                "detailed": "**Adding Your Own Files:**\n                    If you already have mod files, standalone maps, or map packs on your PC, you can attach them directly to specific profiles:\n\n                    1.  Go to the **Game Profiles** tab.\n                    2.  Click the **Edit Profile** button (pencil icon) on any profile card.\n                    3.  Click **Add Local Content**.\n                    4.  Select your mod folder, map archive, or map pack.\n\n                    *This content remains linked to that specific profile without touching other profiles or your base game files.*"
            },
            {
                "title": "The Core: Manifests & CAS",
                "content": "How GenHub manages files and saves disk space.",
                "type": "Concept",
                "detailed": "**How Storage Works:**\n                    GenHub uses content manifests and a shared storage cache to keep your files organized and fast:\n\n                    *   **Content Manifests:** Package manifests clearly list every file, version, and dependency for each mod or patch.\n                    *   **Central Storage Pool (CAS):** Files are stored by content hash in a central cache rather than duplicated across multiple folders.\n                    *   **Deduplication:** When multiple mods use identical textures or game assets, GenHub stores that file once, saving gigabytes of disk space.\n                    *   **Integrity Verification:** Files are verified with checksums before launch to automatically detect and repair corrupted or missing assets."
            },
            {
                "title": "Automated Maintenance",
                "content": "Automatic updates and version compatibility.",
                "type": "Feature",
                "detailed": "**Background Maintenance:**\n                    GenHub handles routine background maintenance automatically:\n\n                    *   **Update Checks:** Automatically checks for service and engine updates before launching your game.\n                    *   **Clean Version Management:** Removes outdated patch files cleanly so your profiles always stay on compatible, tested versions."
            }
        ]
    },
    "profiles": {
        "id": "profiles",
        "title": "Game Profiles",
        "desc": "Create and manage isolated game configurations.",
        "cards": [
            {
                "title": "Your Personal Sandbox",
                "content": "Keep your mods, maps, and game settings isolated and safe.",
                "type": "Concept",
                "detailed": "**Isolated Game Profiles:**\n             A profile is an independent configuration for your game. Instead of reinstalling or swapping files manually:\n\n             1.  **Safety:** Mod files never overwrite your original game installation. If a mod causes problems, your base game remains completely untouched.\n             2.  **Multiple Configurations:** Keep separate profiles for vanilla Zero Hour, Rise of the Reds, ShockWave, or custom balance patches, and switch between them instantly.\n             3.  **Speed:** Workspaces build in milliseconds using file linking, requiring almost zero extra storage on your drive."
            },
            {
                "title": "Controls",
                "content": "Quick reference for profile card buttons.",
                "type": "HowTo",
                "detailed": "**Profile Card Controls:**\n             1.  **Play:** Launches the game with this profile's active mods, settings, and workspace.\n             2.  **Edit Profile (Pencil):** Opens the profile editor to select mods, maps, and adjust game settings.\n             3.  **Copy Profile (Duplicate):** Clones the profile, including all settings and enabled content, into a new profile.\n             4.  **Desktop Shortcut:** Creates a desktop shortcut to launch this profile directly.\n             5.  **Delete Profile:** Removes the profile and its dedicated workspace configuration.\n\n             **Copy Profile Feature:**\n             Cloning creates a complete, independent copy of the profile:\n             -   **Identical Settings:** Video, audio, and control options are duplicated.\n             -   **Identical Content:** All active mods, maps, and patches carry over.\n             -   **Independent Workspace:** Modifying the cloned profile never alters the original.\n\n             **Steam Status:**\n             -   **Gray Icon:** Steam integration is inactive.\n             -   **Blue Icon:** Steam integration is active. Playtime will log to Steam and the Steam Overlay will work in-game."
            },
            {
                "title": "Advanced Profile Options",
                "content": "Custom launch arguments and troubleshooting.",
                "type": "Feature",
                "detailed": "**Launch Arguments:**\n             GenHub passes custom command-line arguments directly to the game. For example, use `-quickstart` to skip introduction videos, or `-win` to force windowed mode.\n\n             **Troubleshooting Logs:**\n             Profile startup and launch logs are recorded in the GenHub AppData directory to help diagnose issues if a game closes unexpectedly."
            }
        ]
    },
    "settings": {
        "id": "settings",
        "title": "Game Settings",
        "desc": "Configure display, audio, and engine settings per profile.",
        "cards": [
            {
                "title": "Standard Audio & Video",
                "content": "Display and audio settings for the Generals engine (Options.ini).",
                "type": "Concept",
                "detailed": "**Display Settings:**\n                    *   **Resolution:** Select your screen resolution. Supports modern widescreen, 1440p, 4K, and Ultrawide displays.\n                    *   **Windowed Mode:** Run in a borderless or standard window for smooth Alt-Tabbing on multi-monitor setups.\n                    *   **Anti-Aliasing & Gamma:** Smooth jagged 3D edges and fine-tune in-game brightness.\n\n                    **Audio & Controls:**\n                    *   **Volume Sliders:** Individual controls for Master, Sound Effects, Music, and Voice levels.\n                    *   **Sound Channels:** Maximum simultaneous audio channels (supports up to 128 channels on modern systems).\n                    *   **Right-Click Attack:** Switch between classic left-click and modern RTS right-click command schemes.\n                    *   **Scroll Speed:** Customize camera movement speed at screen borders."
            },
            {
                "title": "TheSuperHackers Engine",
                "content": "Modern client extensions and stability improvements.",
                "type": "Feature",
                "detailed": "**Community Engine Enhancements:**\n                    TheSuperHackers (TSH) engine is the active community codebase improving Zero Hour stability and modern feature support.\n\n                    **Engine Improvements:**\n                    *   **Cursor Clip:** Restricts the mouse to the game window during matches to prevent accidental clicks on a second monitor.\n                    *   **Windowed Edge Scrolling:** Enables smooth camera scrolling at window edges even in windowed mode.\n                    *   **Font Scaling:** Automatically scales in-game text and UI for high-DPI and 4K displays.\n\n                    **In-Game Overlays:**\n                    *   **Economy Stats:** Live resources-per-minute income rate display.\n                    *   **Performance Metrics:** On-screen clock, FPS counter, and network latency indicators.\n                    *   **Replay Archiving:** Automatically saves and structures match replays into categorized folders."
            },
            {
                "title": "GeneralsOnline Features",
                "content": "Online multiplayer lobby and matchmaking features.",
                "type": "Feature",
                "detailed": "**Modern Multiplayer Integration:**\n                    Generals Online provides dedicated online matchmaking, lobbies, and community rankings for Command & Conquer: Generals and Zero Hour.\n\n                    **Lobby & Networking:**\n                    *   **Ping & Ranks:** View player latency and competitive ladder rankings directly in the lobby.\n                    *   **Seamless Login:** Connect securely using Steam, Discord, or GameReplays authentication.\n                    *   **Desktop Notifications:** Receive alerts when friends come online or invite you to matches.\n                    *   **Chat Options:** Adjust lobby text size and fade delays to your preference.\n\n                    **In-Game Camera:**\n                    *   **Camera Zoom Height:** Customize maximum camera zoom distance for broader battlefield visibility.\n                    *   **Camera Pan Speed:** Tune panning sensitivity during multiplayer matches."
            }
        ]
    },
    "content": {
        "id": "content",
        "title": "Profile Content",
        "desc": "Manage mods, maps, and patches enabled for each profile.",
        "cards": [
            {
                "title": "Content Types & Hierarchy",
                "content": "Understand the roles and priority of each content type.",
                "type": "Concept",
                "detailed": "**Game Client:**\n                    The base game files (Generals or Zero Hour) installed on your system. Every profile uses a client as its base foundation.\n\n                    **Mod:**\n                    A major modification changing gameplay, factions, and units (e.g. Rise of the Reds, ShockWave). A profile typically centers around one primary mod.\n\n                    **Map:**\n                    An individual custom map file for skirmish and multiplayer battles.\n\n                    **Map Pack:**\n                    A bundled collection of maps. Using a map pack lets you toggle an entire tournament pool or custom map collection with a single checkbox.\n\n                    **Patch:**\n                    An engine or system-level enhancement that improves stability (such as the 4GB Memory Patch or GenTool).\n\n                    **Addon:**\n                    Supplementary visual or audio packs (such as remastered music or HD textures) that sit on top of mods safely.\n\n                    **Tool:**\n                    External utilities (such as World Builder or FinalBIG) that can be opened directly from your profile dashboard."
            },
            {
                "title": "Cloning Content",
                "content": "How copying profiles preserves your content setup.",
                "type": "Concept",
                "detailed": "**How Content Duplication Works:**\n                    When you use **Copy Profile**, GenHub duplicates your profile's content configuration:\n\n                    *   **Preserved Content:** All active mods, maps, and patches are mirrored into the new profile.\n                    *   **Independent Editing:** The clone is fully independent. Adding or removing content in the clone will not change the original profile.\n                    *   **Zero Storage Waste:** File linking ensures that cloning a profile does not copy large mod files on your disk. Both profiles reference the shared storage cache."
            },
            {
                "title": "Content Editor",
                "content": "Adding and ordering content in a profile.",
                "type": "HowTo",
                "detailed": "**Content Workflow:**\n                    1.  **Available Content (Bottom):** Shows installed mods, maps, and patches you can add to this profile.\n                    2.  **Enabled Content (Top):** Shows content currently active for this profile.\n                    3.  **Load Priority:** Content is applied from top to bottom. Items higher in the list take priority if two packages contain conflicting files.\n                    4.  **Add Local:** Link external folders or archives without copying them into GenHub."
            },
            {
                "title": "Virtual File System",
                "content": "How files are merged when launching.",
                "type": "Feature",
                "detailed": "**Layered File Merging:**\n                    When you click Play, GenHub combines all active content into a unified workspace:\n\n                    1.  **Base Layer:** Game client files form the foundation.\n                    2.  **Mod Layer:** Mod files overlay and replace base game assets.\n                    3.  **Top Layer:** Custom maps, addons, and patches apply with highest priority."
            }
        ]
    },
    "shortcuts": {
        "id": "shortcuts",
        "title": "Desktop Shortcuts",
        "desc": "Create one-click desktop shortcuts for your profiles.",
        "cards": [
            {
                "title": "Headless Mode Launcher",
                "content": "Launch profiles directly from your desktop.",
                "type": "Concept",
                "detailed": "**Direct Desktop Launching:**\n             Shortcuts allow you to start any mod configuration straight from your desktop without keeping the main launcher window open:\n\n             1.  **Direct Launch:** Double-click the shortcut to start the game immediately.\n             2.  **Silent Setup:** GenHub runs briefly in the background to prepare the profile workspace, then hands off to the game.\n             3.  **Clean Exit:** Workspace temporary files are automatically cleaned up when the game closes."
            },
            {
                "title": "Shortcut Creation",
                "content": "How to add a profile shortcut to your desktop.",
                "type": "HowTo",
                "detailed": "**Creating a Shortcut:**\n             1.  In **Game Profiles**, right-click any profile card (or click the Desktop shortcut icon).\n             2.  Select **Create Desktop Shortcut**.\n             3.  A standard Windows shortcut (`.lnk`) appears on your desktop.\n             4.  Double-clicking this shortcut launches that specific profile configuration immediately."
            },
            {
                "title": "Icon Customization",
                "content": "Visual icons for your desktop shortcuts.",
                "type": "Feature",
                "detailed": "**Shortcut Icons:**\n             GenHub extracts official high-resolution icon resources from the game executable (`generals.exe` or `game.dat`).\n             If your profile uses custom metadata or mod artwork, GenHub converts that image into an icon embedded directly in the shortcut."
            }
        ]
    },
    "steam": {
        "id": "steam",
        "title": "Steam Integration",
        "desc": "Track playtime and use the Steam Overlay with mods.",
        "cards": [
            {
                "title": "AppID Injection",
                "content": "Use Steam playtime tracking and the overlay with any mod.",
                "type": "Concept",
                "detailed": "**Steam Integration:**\n             GenHub connects your mod launches with Steam so you can take advantage of Steam community features:\n\n             *   **Steam Overlay:** Chat with friends, join invites, and take screenshots in-game.\n             *   **Friend Status:** Displays Command & Conquer: Generals as your current game.\n             *   **Playtime Tracking:** Hours played with mods count toward your official Steam library stats."
            },
            {
                "title": "Usage Requirements",
                "content": "Requirements for Steam integration.",
                "type": "HowTo",
                "detailed": "**Prerequisites:**\n             To use Steam features:\n             1.  The **Steam desktop application** must be running before launching the game.\n             2.  The active Steam account must own *Command & Conquer: The Ultimate Collection*.\n\n             *Note: If Steam is not running, GenHub will launch the profile in standard mode without interruption.*"
            },
            {
                "title": "Time Tracking",
                "content": "Steam playtime logging across mod profiles.",
                "type": "Feature",
                "detailed": "**Playtime Tracking:**\n             Because Steam recognizes the game through GenHub's launcher, all playtime across your various mods, map packs, and profiles is logged to your Steam library."
            }
        ]
    },
    "local": {
        "id": "local",
        "title": "Local Content",
        "desc": "Import external mods, custom engine builds, modding tools, and maps.",
        "cards": [
            {
                "title": "Importing local content into your library",
                "content": "Add folders, ZIP archives, and executables as reusable content items.",
                "type": "Concept",
                "detailed": "**The Add Local workflow**\n                    Use the **Add Local** button in the profile Content tab or library view to register external files into GenHub:\n\n                    * **Folders:** Select an unpacked mod directory or community tool folder on your drive.\n                    * **ZIP archives:** Select or drop an archive. GenHub extracts files into an isolated staging area for inspection.\n                    * **Executables:** Choose a standalone `.exe` such as WorldBuilder or an engine binary.\n\n                    **Central content storage**\n                    When you confirm an import, GenHub registers the item into your local content pool and creates an immutable manifest. The content item is stored once on disk and can be attached to any number of game profiles without copying or duplicating files."
            },
            {
                "title": "Content types and executable selection",
                "content": "Configure mods, addons, maps, modding tools, and game clients.",
                "type": "Feature",
                "detailed": "**Selecting the correct content type**\n                    The content type determines how GenHub mounts and runs your files:\n\n                    * **Mods:** Full game modifications containing `.big` archives (such as ShockWave, Rise of the Reds, or Contra), INI overrides, and custom art assets.\n                    * **Addons and patches:** Incremental additions such as camera height adjustments, texture packs, or balance patches that layer over base games or mods.\n                    * **Maps and map packs:** Loose `.map` files with `.tga` preview images or bundled map archives. GenHub indexes map metadata and makes them available across profiles.\n                    * **Modding tools:** Utilities like GenHotkeys, FinalBIG, or BigViewer. For modding tools, the content preview tree displays a **Select** button next to each `.exe`. Clicking **Select** designates the primary executable so GenHub can launch the tool directly from the profile tool tray.\n                    * **Game clients and executables:** Custom game binaries, such as community test builds from TheSuperHackers, or standalone editors like WorldBuilder. Marking the main executable tells GenHub which binary starts the game client or editor."
            },
            {
                "title": "GenLauncher file normalization",
                "content": "Detect and repair scrambled .gib archives and suffix-renamed files.",
                "type": "HowTo",
                "detailed": "**Why normalization is necessary**\n                    GenLauncher modifies files directly inside the game directory when activating and deactivating mods. It renames active `.big` files to `.gib` to scramble them, appends `.GLR` (replaced files), `.GOF` (original file backups), and `.GLTC` (temporary copies) suffixes, and creates stray symbolic links. Importing a directory left in this state prevents the game engine from reading mod archives.\n\n                    **Automated normalization in GenHub**\n                    When you select a folder or archive containing GenLauncher files, GenHub's normalization service identifies these artifacts automatically during staging:\n\n                    1. Renames all scrambled `.gib` archives back to standard `.big` files so the game engine can mount them.\n                    2. Strips `.GLR`, `.GOF`, and `.GLTC` suffixes to restore standard file names.\n                    3. Cleans up broken or invalid symbolic links left by previous installations.\n\n                    Normalization runs safely in the staging area before registration, ensuring your imported content item contains clean standard assets."
            },
            {
                "title": "Profile linking and workspace isolation",
                "content": "Link local items to game profiles without modifying base game files.",
                "type": "Feature",
                "detailed": "**Linking content to game profiles**\n                    After registering a local content item, open any profile in **Profile Settings** and navigate to the **Content** tab:\n\n                    * Enable the checkbox next to any mod, addon, tool, or map pack to attach it to that profile.\n                    * Reorder items in the list to configure load priority when multiple items override the same INI settings or art assets.\n                    * Assign custom game clients (such as a test build from TheSuperHackers) in the Client selector.\n\n                    **Workspace isolation**\n                    GenHub never writes modded files into your original Command & Conquer installation directory. When launching a profile, GenHub creates an isolated workspace using symbolic links or hardlinks to combine your base game with the specific content items assigned to that profile. Your base game files remain untouched, and profiles run independently without file conflicts."
            }
        ]
    },
    "tools": {
        "id": "tools",
        "title": "Tools & Utilities",
        "desc": "Inspect replays and manage custom maps directly.",
        "cards": [
            {
                "title": "Replay Manager: CRC Catalog & Compatibility",
                "content": "Binary SAGE header parsing and 138 gameclient catalog matching.",
                "type": "Feature",
                "detailed": "**CRC Mapping Infrastructure (PR #422):**\n                    *   **Header Parsing:** GenHub reads the binary SAGE replay header (`GENREP`) to extract Exe CRC, INI CRC, build timestamp, and map metadata.\n                    *   **138 GameClient Catalog:** Automatically compares parsed checksums against an embedded catalog spanning TheSuperHackers (98 weekly releases), GeneralsOnline (31 periodic/QFE/EAC releases), and 9 Retail/Steam/EA/CommunityOutpost variants.\n                    *   **Live Compatibility Resolution:**\n                        *   **Compatible (Green):** Executable and game data match an active profile — ready for instant 1-click launch.\n                        *   **Requires Profile (Blue):** The required gameclient exists on disk; GenHub auto-creates a dedicated isolated replay profile.\n                        *   **Downloadable (Orange):** Missing client or patch manifests can be directly acquired from the community catalog.\n                    *   **Desync Prevention:** Eliminates mismatch errors and ensures replays play back with perfect fidelity."
            },
            {
                "title": "Replay Manager: Import & Parse",
                "content": "Import and inspect game recordings.",
                "type": "Concept",
                "detailed": "**Importing Replays:**\n                    *   **Match ID or URL:** Paste a Match ID, GenTool URL, or replay download link and click **Download**.\n                    *   **Browse:** Select `.rep` files or `.zip` archives from your PC.\n                    *   **Drag & Drop:** Drop replay files directly into the Replay Manager window.\n\n                    **Replay Details:**\n                    *   GenHub inspects replay file headers to show the map name, players, and game version before you watch."
            },
            {
                "title": "Replay Manager: Cloud & Sharing",
                "content": "Upload and share replays with other players.",
                "type": "Feature",
                "detailed": "**Cloud Sharing:**\n                    *   Select replays and click **Upload** to upload them to secure cloud storage.\n                    *   A shareable download link is automatically copied to your clipboard.\n\n                    **Upload History:**\n                    *   Review recently uploaded replays.\n                    *   Copy download links again or remove expired entries from your list."
            },
            {
                "title": "Replay Manager: Archiving",
                "content": "Zip and unzip replay collections.",
                "type": "HowTo",
                "detailed": "**Creating Archives:**\n                    *   Select multiple replays and click **Zip** to compress them into an archive for sharing or tournament submissions.\n\n                    **Extracting Archives:**\n                    *   Select a `.zip` archive in the replay list and click **Uncompress** to extract all `.rep` files directly into your replay folder."
            },
            {
                "title": "Map Manager: Library",
                "content": "Browse and organize custom maps.",
                "type": "Concept",
                "detailed": "**Map Management:**\n                    *   **Search:** Filter custom maps quickly by name or folder.\n                    *   **Minimap Previews:** Displays map preview thumbnails extracted directly from map files.\n                    *   **Import:** Drag and drop map folders or `.zip` archives to install them instantly.\n\n                    **Actions:**\n                    *   **Delete:** Remove unused maps from your drive.\n                    *   **Open Folder:** Open the specific map folder in Windows Explorer."
            },
            {
                "title": "Map Manager: Map Packs",
                "content": "Organize maps into reusable collections.",
                "type": "Feature",
                "detailed": "**What is a Map Pack?**\n                    A Map Pack bundles multiple maps together (such as a tournament map pool or 4-player FFA collection).\n\n                    **Creating a Map Pack:**\n                    1.  Select multiple maps with `Ctrl+Click` or `Shift+Click`.\n                    2.  Click **Pack** in the top-right toolbar.\n                    3.  Enter a name and click **Create MapPack**.\n\n                    Once created, you can toggle the entire map collection on or off for any profile in one click."
            },
            {
                "title": "Hotkeys Editor: Visual Keymapping & Overlays",
                "content": "Port of GenHotkeys with visual command button rebinding and cameo icon overlays.",
                "type": "Feature",
                "detailed": "**Visual Hotkeys Customization (PR #451):**\n                    *   **Faction Support:** Configure commands across USA, China, and GLA including all Zero Hour specialized generals.\n                    *   **Category Filtering:** Filter across Buildings, Units, Upgrades, and Tactical Abilities.\n                    *   **Cameo Icon Overlays:** Renders stamped hotkey letter badges directly onto command button textures (`.tga`), placing key indicators in any corner of the icon.\n                    *   **Preset Layouts:** Choose from competitive Grid layouts (`QWER` / `ASDF`), classic retail keybindings, or create customized profiles."
            },
            {
                "title": "Hotkeys Editor: Conflict Detection & Addon Export",
                "content": "Real-time key conflict detection and 1-click BIG archive packaging into CAS.",
                "type": "Feature",
                "detailed": "**Conflict Engine & CAS Export (PR #451):**\n                    *   **Real-time Conflict Detection:** Automatically detects conflicting key assignments within shared command sets and provides a Next Conflict navigation shortcut.\n                    *   **1-Click Addon Generation:** Compiles customized CommandSet overrides, String table updates, and generated TGA button overlays into an isolated `!Hotkeys_<Profile>_<Game>.big` archive.\n                    *   **CAS Registration:** Automatically registers the generated archive into GenHub's Content Addressable Storage and mounts it with high load priority over base game data."
            }
        ]
    },
    "scangames": {
        "id": "scangames",
        "title": "Game Detection",
        "desc": "Automatically detect and verify game installations.",
        "cards": [
            {
                "title": "Auto-Detection",
                "content": "How GenHub locates installed games on your computer.",
                "type": "Concept",
                "detailed": "**Detection Methods:**\n                    GenHub locates game installations by scanning:\n                    1.  **Steam Libraries:** Automatically detects Steam installations of Command & Conquer: The Ultimate Collection.\n                    2.  **EA App / Origin:** Locates official EA App install directories and registry records.\n                    3.  **Classic CD & Retail:** Checks standard installation paths and registry keys for classic disk editions.\n\n                    If your game is installed in a custom location, click **Browse** to link its folder manually."
            },
            {
                "title": "Signature Verification",
                "content": "Integrity checks and version verification.",
                "type": "Feature",
                "detailed": "**Binary Verification:**\n                    GenHub calculates SHA-256 hashes of `generals.exe` and `game.dat` to confirm game versions and file integrity.\n                    *   **Verified:** Matches known official releases (such as Steam edition, EA App, The First Decade, or v1.04).\n                    *   **Unverified:** Custom or unrecognized binaries are labeled as Unverified, but remain fully launchable."
            }
        ]
    },
    "workspaces": {
        "id": "workspaces",
        "title": "Virtual Workspaces",
        "desc": "Workspace strategies, file linking techniques, and isolation mechanics.",
        "cards": [
            {
                "title": "The Magic Mirror",
                "content": "Understanding how isolated game workspaces work.",
                "type": "Concept",
                "detailed": "**How Workspaces Work:**\n                    When you click Play, GenHub instantly prepares a dedicated workspace folder for that specific profile.\n\n                    **Key Benefits:**\n                    1.  **Zero Extra Disk Space:** In linked modes (HardLink and SymlinkOnly), the workspace functions as a complete multi-gigabyte game folder while consuming virtually 0 MB of extra disk space.\n                    2.  **Complete Profile Isolation:** Mods and configurations live in dedicated profile workspaces. Your main game directory remains untouched, so files never get mixed up. (For mods that modify game binaries in-place, select Hybrid or Full Copy mode).\n                    3.  **Instant Switching:** Switch between large total conversions like *Rise of the Reds* and *ShockWave* in seconds without reinstalling or moving files."
            },
            {
                "title": "Workspace Strategies Compared",
                "content": "Comparing HardLink, SymlinkOnly, HybridCopySymlink, and FullCopy strategies.",
                "type": "Concept",
                "detailed": "**Choosing the Right Strategy:**\n                    GenHub supports four file linking strategies under **Settings -> Game Configuration**:\n\n                    *   **HardLink (Default & Recommended):**\n                        *   *How it works:* Creates direct filesystem pointers (hard links) on the same drive. If your workspace and game files are on different drives, GenHub automatically falls back to copying files.\n                        *   *Disk Space:* **0 bytes** extra storage when on the same drive (copies if across different drives).\n                        *   *Speed:* Instant (< 50ms) on the same volume.\n                        *   *Privileges:* No administrator privileges or Developer Mode required.\n                        *   *Recommendation:* Keep your workspaces and game installation on the **same drive** (e.g. both on `C:` or both on `D:`) for optimal zero-space performance.\n\n                    *   **SymlinkOnly:**\n                        *   *How it works:* Creates symbolic links pointing to source files and directories.\n                        *   *Disk Space:* **Negligible** (~a few KB of link pointers).\n                        *   *Speed:* Instant (< 50ms).\n                        *   *Advantage:* Links seamlessly across **different drives and partitions**.\n                        *   *Requirement:* On Windows, requires **Administrator rights** or **Developer Mode** enabled in Windows Settings.\n\n                    *   **HybridCopySymlink (Balanced Compatibility):**\n                        *   *How it works:* Copies essential engine files, scripts, and configuration files into the workspace while symlinking large media files (textures, audio, and video).\n                        *   *Disk Space:* Balanced footprint (copies key configs, links media).\n                        *   *Speed:* Fast (1-2 seconds).\n                        *   *Advantage:* Protects configuration files from cross-profile conflicts while keeping disk usage low.\n\n                    *   **FullCopy (Universal Fallback):**\n                        *   *How it works:* Physically copies every game and mod file into the workspace directory.\n                        *   *Disk Space:* Uses the full game size (**2-5+ GB** per profile).\n                        *   *Speed:* Slower (10-30+ seconds depending on drive speed).\n                        *   *Advantage:* Maximum compatibility across external drives, network drives, and restricted environments."
            },
            {
                "title": "Hardlinks vs Symlinks vs Copies: Deep Dive",
                "content": "How file linking differs under the hood.",
                "type": "Feature",
                "detailed": "**How Linking Works Under the Hood:**\n\n                    *   **Hardlink:**\n                        A hardlink points directly to the existing file data on disk at the filesystem level. Because the underlying file data is shared, creating a hardlink takes zero extra storage. Hardlinks must reside on the same drive partition as the original file.\n\n                    *   **Symlink (Symbolic Link):**\n                        A symlink is a lightweight pointer that stores a path to the target file or folder, similar to a transparent operating system shortcut. Symlinks can cross different drives, but Windows security policies require elevated privileges or Developer Mode to create them.\n\n                    *   **Full Copy:**\n                        A complete duplicate of the file written to a new location on disk.\n\n                    **Automatic Fallback:**\n                    If you configure Symlink mode but run GenHub without administrator rights or Developer Mode, GenHub automatically falls back to hardlinks when files reside on the same drive, ensuring your game launches without interruption."
            },
            {
                "title": "Troubleshooting & Permissions",
                "content": "Resolving common permissions and workspace build errors.",
                "type": "HowTo",
                "detailed": "**Common Issues & Solutions:**\n\n                    *   **\"Access Denied\" or Privilege Errors:**\n                        *   If using the Symlink strategy on Windows, enable **Developer Mode** in *Windows Settings -> System -> For developers*, or run GenHub as Administrator.\n                        *   Alternatively, switch your Default Workspace Strategy to **HardLink** in GenHub Settings.\n                    *   **Cross-Drive Linking & Storage:**\n                        *   Hardlinks require both the game files and workspace to be on the same drive volume to achieve zero-space linking. If they are on different drives, GenHub falls back to copying files.\n                        *   To keep workspaces fast and zero-space, place your CAS pool and workspace directories on the same drive as your game installation in **Settings -> Data Directories**, or enable Developer Mode for symlinks.\n                    *   **\"File In Use\" / Locked File Warnings:**\n                        *   Make sure all instances of `generals.exe` and `game.dat` are closed before switching profiles or rebuilding workspaces."
            },
            {
                "title": "Performance Specs",
                "content": "Efficiency, speed, and integrity metrics across strategies.",
                "type": "Feature",
                "detailed": "**Strategy Performance Summary:**\n\n                    *   **HardLink:**\n                        *   *Creation Time:* < 50ms on same volume\n                        *   *Disk Overhead:* 0 MB on same volume (copies if across different drives)\n                        *   *Integrity:* Shared data clusters (CAS objects remain immutable in the cache)\n                    *   **SymlinkOnly:**\n                        *   *Creation Time:* < 50ms\n                        *   *Disk Overhead:* < 1 MB\n                        *   *Integrity:* Pointer redirection across drives\n                    *   **Hybrid:**\n                        *   *Creation Time:* 1-2 seconds\n                        *   *Disk Overhead:* Small (copies essential configs, links media assets)\n                        *   *Integrity:* Isolated configs, shared media links\n                    *   **Full Copy:**\n                        *   *Creation Time:* 10-30 seconds\n                        *   *Disk Overhead:* Full game size (2,000 - 5,000+ MB)\n                        *   *Integrity:* Total physical file separation"
            }
        ]
    },
    "appupdates": {
        "id": "appupdates",
        "title": "App Updates",
        "desc": "Manage launcher updates and release channels.",
        "cards": [
            {
                "title": "Version Control",
                "content": "Official releases and update checking.",
                "type": "Concept",
                "detailed": "**How Updates Work:**\n             GenHub checks for updates automatically from official GitHub releases. When a new version is published, GenHub verifies the release and displays an update notification."
            },
            {
                "title": "Update Workflow",
                "content": "Applying updates seamlessly.",
                "type": "HowTo",
                "detailed": "**Update Process:**\n             1.  **Notification:** An update banner appears when a new release is available.\n             2.  **Background Download:** Updates download quietly in the background without interrupting your gameplay.\n             3.  **Fast Restart:** Clicking **Restart** applies the update in seconds and restores your launcher session."
            },
            {
                "title": "Rollback Capability",
                "content": "How to revert to an earlier release if needed.",
                "type": "Feature",
                "detailed": "**Reverting to Previous Versions:**\n             GenHub automatically preserves your profile configurations and settings during updates. If you ever need to use an earlier build, download the previous release archive from GitHub and extract it into your GenHub installation directory."
            }
        ]
    },
    "changelog": {
        "id": "changelog",
        "title": "Changelog",
        "desc": "Version history.",
        "cards": [
            {
                "title": "GenHub Alpha 3 (v0.0.3)",
                "content": "Latest release with CAS storage engine and multi-publisher downloads.",
                "type": "Changelog",
                "detailed": "**v0.0.3 Alpha 3 Release Notes:**\n* New ContentDetailView with dependency inspection\n* MapManager DataGrid with SHA-256 integrity verification\n* Multi-publisher catalog with 6 providers\n* Virtual Workspaces with NTFS hardlinks\n* Replay Manager CRC matching"
            },
            {
                "title": "GenHub Alpha 2 (v0.0.2)",
                "content": "Initial public preview release with setup wizard.",
                "type": "Changelog",
                "detailed": "**v0.0.2 Release Notes:**\n* Game profile creation wizard\n* Steam launch integration\n* DirectX 9 / Vulkan configuration\n* Initial download repository"
            }
        ]
    },
    "faq": {
        "id": "faq",
        "title": "Frequently Asked Questions",
        "desc": "Common questions about the Generals Online service.",
        "cards": [
            {
                "title": "What is Generals Online?",
                "content": "Generals Online is a modern multiplayer and lobby platform for Command & Conquer: Generals and Zero Hour.",
                "type": "Concept",
                "detailed": "Generals Online replaces the discontinued GameSpy service with modern multiplayer matchmaking, lobby features, automatic updates, and ladder rankings\u2014preserving classic gameplay while delivering stable online play on modern PCs."
            },
            {
                "title": "Do I need a clean install of Zero Hour?",
                "content": "No. Generals Online works alongside your existing installation.",
                "type": "HowTo",
                "detailed": "You do not need a fresh game installation or to delete existing files. GenHub isolates Generals Online so your base game files remain untouched."
            },
            {
                "title": "Can I play Generals Online if I have GenTool or GenPatcher installed?",
                "content": "Yes. Generals Online is fully compatible with GenTool and GenPatcher.",
                "type": "Concept",
                "detailed": "Generals Online runs in its own profile environment and works alongside GenTool widescreen and anti-cheat features without conflicts."
            },
            {
                "title": "Can I use custom UI or control bars?",
                "content": "Yes. Custom UI assets and control bars are supported.",
                "type": "Concept",
                "detailed": "Custom UI modifications, such as HUD control bars, work normally in Generals Online."
            },
            {
                "title": "Does Generals Online modify my original game files?",
                "content": "No. Your original installation files are never modified.",
                "type": "Concept",
                "detailed": "Generals Online runs from an isolated profile workspace. Your main game folder remains clean and untouched."
            },
            {
                "title": "Are custom maps supported?",
                "content": "Yes. Custom maps and in-lobby map transfers are supported.",
                "type": "Feature",
                "detailed": "Generals Online supports in-game and lobby map downloads so you can play custom maps with other players seamlessly."
            },
            {
                "title": "How do I launch Generals Online?",
                "content": "Launch through GenHub or your profile desktop shortcut.",
                "type": "HowTo",
                "detailed": "Select your Generals Online profile in GenHub and click Play, or launch it directly with a desktop shortcut created from that profile."
            },
            {
                "title": "Which game versions are supported?",
                "content": "Developed and tested for official Steam and EA App / Origin releases.",
                "type": "Concept",
                "detailed": "Generals Online is designed for official Steam and EA releases. For the best experience and easiest setup, the Steam release of Command & Conquer: The Ultimate Collection is recommended."
            },
            {
                "title": "How do I log in?",
                "content": "Sign in securely using Steam, Discord, or GameReplays.",
                "type": "HowTo",
                "detailed": "Generals Online uses OpenID authentication. You authenticate directly through Steam, Discord, or GameReplays\u2014your account passwords are never seen or stored by Generals Online."
            },
            {
                "title": "Is logging in safe?",
                "content": "Yes. OpenID ensures your account password remains completely private.",
                "type": "Concept",
                "detailed": "OpenID only transmits a secure account identifier to verify your identity. Your login credentials are handled directly by Steam, Discord, or GameReplays."
            },
            {
                "title": "How do I check if the service is online?",
                "content": "Check the in-game status, the community Discord, or the status page.",
                "type": "Feature",
                "detailed": "Live service status is shown on the login screen, with real-time announcements available on the community Discord."
            },
            {
                "title": "How do I report bugs or suggest features?",
                "content": "Join the community Discord to submit feedback.",
                "type": "HowTo",
                "detailed": "The development team actively tracks issues and community suggestions in dedicated Discord channels."
            },
            {
                "title": "How are updates delivered?",
                "content": "Updates download automatically through the launcher.",
                "type": "Feature",
                "detailed": "When an update is released, GenHub detects and applies it so you are always on the latest version."
            },
            {
                "title": "Do I need third-party VPN tools (Hamachi, Radmin, GameRanger)?",
                "content": "No. Online matchmaking is built directly into the service.",
                "type": "Concept",
                "detailed": "Generals Online includes native networking and matchmaking. You do not need third-party virtual LAN software or external wrappers to play online."
            },
            {
                "title": "Do I need to forward router ports?",
                "content": "No. Built-in NAT traversal connects players automatically.",
                "type": "Concept",
                "detailed": "Modern NAT traversal handles player connections automatically without requiring manual port forwarding on your home router."
            },
            {
                "title": "Is network communication secure?",
                "content": "Yes. Game traffic is encrypted using AES-256.",
                "type": "Feature",
                "detailed": "Network traffic uses industry-standard AES-256-GCM encryption, providing significantly better security than the original game engine's unencrypted packets."
            },
            {
                "title": "Why did Windows Firewall prompt for permission?",
                "content": "Windows prompts when a new app accesses the network for the first time.",
                "type": "HowTo",
                "detailed": "When connecting to multiplayer servers for the first time, Windows Firewall asks to allow network access. Click Allow to enable online connectivity."
            },
            {
                "title": "What are connection relays?",
                "content": "Relays route traffic when direct peer-to-peer connections are blocked.",
                "type": "Concept",
                "detailed": "If two players have strict firewalls that prevent direct peer-to-peer connection, traffic routes seamlessly through community relay servers (similar to Steam networking or CNCNet tunnels)."
            },
            {
                "title": "Do relays cause lag or performance drops?",
                "content": "Typically no. Relays use high-bandwidth, low-latency backbone servers.",
                "type": "Concept",
                "detailed": "Relay servers are hosted on high-speed backbones and often provide comparable or better latency than congested direct peer-to-peer routes."
            },
            {
                "title": "How does the game select which relay to use?",
                "content": "Relay connections are formed dynamically on a player-to-player basis.",
                "type": "Feature",
                "detailed": "Relay connections are established dynamically per player pair, selecting the server location with the lowest latency for that match. Users in the same lobby can connect through different regional edge nodes to achieve optimal ping."
            },
            {
                "title": "Are relays secure?",
                "content": "Yes. Relays cannot decrypt match traffic.",
                "type": "Feature",
                "detailed": "Relay servers forward encrypted packets and do not have access to the encryption keys required to read or inspect traffic."
            },
            {
                "title": "Can I host a relay?",
                "content": "Community relay hosting is not needed at this time.",
                "type": "Concept",
                "detailed": "Generals Online operates on global edge infrastructure spanning hundreds of data centers worldwide, delivering low latency without requiring community relay hosting."
            }
        ]
    },
    "gochange": {
        "id": "gochange",
        "title": "Generals Online Changelog",
        "desc": "View the latest changes and updates to the Generals Online service.",
        "cards": [
            {
                "title": "v2.1.4 Competitive Update",
                "content": "Ranked ladder calibration and ping optimization.",
                "type": "Changelog",
                "detailed": "**v2.1.4 Changelog:**\n* Optimized server relay latency for EU and NA players\n* Fixed spectator mode desync during superweapon detonations\n* Added automatic disconnect detection and ladder Elo adjustment\n* GenTool 8.9 widescreen compatibility update"
            }
        ]
    },
    "gofaq": {
        "id": "gofaq",
        "title": "Frequently Asked Questions",
        "desc": "Common questions about the Generals Online service.",
        "cards": [
            {
                "title": "What is Generals Online?",
                "content": "Generals Online is a modern multiplayer and lobby platform for Command & Conquer: Generals and Zero Hour.",
                "type": "Concept",
                "detailed": "Generals Online replaces the discontinued GameSpy service with modern multiplayer matchmaking, lobby features, automatic updates, and ladder rankings\u2014preserving classic gameplay while delivering stable online play on modern PCs."
            },
            {
                "title": "Do I need a clean install of Zero Hour?",
                "content": "No. Generals Online works alongside your existing installation.",
                "type": "HowTo",
                "detailed": "You do not need a fresh game installation or to delete existing files. GenHub isolates Generals Online so your base game files remain untouched."
            },
            {
                "title": "Can I play Generals Online if I have GenTool or GenPatcher installed?",
                "content": "Yes. Generals Online is fully compatible with GenTool and GenPatcher.",
                "type": "Concept",
                "detailed": "Generals Online runs in its own profile environment and works alongside GenTool widescreen and anti-cheat features without conflicts."
            },
            {
                "title": "Can I use custom UI or control bars?",
                "content": "Yes. Custom UI assets and control bars are supported.",
                "type": "Concept",
                "detailed": "Custom UI modifications, such as HUD control bars, work normally in Generals Online."
            },
            {
                "title": "Does Generals Online modify my original game files?",
                "content": "No. Your original installation files are never modified.",
                "type": "Concept",
                "detailed": "Generals Online runs from an isolated profile workspace. Your main game folder remains clean and untouched."
            },
            {
                "title": "Are custom maps supported?",
                "content": "Yes. Custom maps and in-lobby map transfers are supported.",
                "type": "Feature",
                "detailed": "Generals Online supports in-game and lobby map downloads so you can play custom maps with other players seamlessly."
            },
            {
                "title": "How do I launch Generals Online?",
                "content": "Launch through GenHub or your profile desktop shortcut.",
                "type": "HowTo",
                "detailed": "Select your Generals Online profile in GenHub and click Play, or launch it directly with a desktop shortcut created from that profile."
            },
            {
                "title": "Which game versions are supported?",
                "content": "Developed and tested for official Steam and EA App / Origin releases.",
                "type": "Concept",
                "detailed": "Generals Online is designed for official Steam and EA releases. For the best experience and easiest setup, the Steam release of Command & Conquer: The Ultimate Collection is recommended."
            },
            {
                "title": "How do I log in?",
                "content": "Sign in securely using Steam, Discord, or GameReplays.",
                "type": "HowTo",
                "detailed": "Generals Online uses OpenID authentication. You authenticate directly through Steam, Discord, or GameReplays\u2014your account passwords are never seen or stored by Generals Online."
            },
            {
                "title": "Is logging in safe?",
                "content": "Yes. OpenID ensures your account password remains completely private.",
                "type": "Concept",
                "detailed": "OpenID only transmits a secure account identifier to verify your identity. Your login credentials are handled directly by Steam, Discord, or GameReplays."
            },
            {
                "title": "How do I check if the service is online?",
                "content": "Check the in-game status, the community Discord, or the status page.",
                "type": "Feature",
                "detailed": "Live service status is shown on the login screen, with real-time announcements available on the community Discord."
            },
            {
                "title": "How do I report bugs or suggest features?",
                "content": "Join the community Discord to submit feedback.",
                "type": "HowTo",
                "detailed": "The development team actively tracks issues and community suggestions in dedicated Discord channels."
            },
            {
                "title": "How are updates delivered?",
                "content": "Updates download automatically through the launcher.",
                "type": "Feature",
                "detailed": "When an update is released, GenHub detects and applies it so you are always on the latest version."
            },
            {
                "title": "Do I need third-party VPN tools (Hamachi, Radmin, GameRanger)?",
                "content": "No. Online matchmaking is built directly into the service.",
                "type": "Concept",
                "detailed": "Generals Online includes native networking and matchmaking. You do not need third-party virtual LAN software or external wrappers to play online."
            },
            {
                "title": "Do I need to forward router ports?",
                "content": "No. Built-in NAT traversal connects players automatically.",
                "type": "Concept",
                "detailed": "Modern NAT traversal handles player connections automatically without requiring manual port forwarding on your home router."
            },
            {
                "title": "Is network communication secure?",
                "content": "Yes. Game traffic is encrypted using AES-256.",
                "type": "Feature",
                "detailed": "Network traffic uses industry-standard AES-256-GCM encryption, providing significantly better security than the original game engine's unencrypted packets."
            },
            {
                "title": "Why did Windows Firewall prompt for permission?",
                "content": "Windows prompts when a new app accesses the network for the first time.",
                "type": "HowTo",
                "detailed": "When connecting to multiplayer servers for the first time, Windows Firewall asks to allow network access. Click Allow to enable online connectivity."
            },
            {
                "title": "What are connection relays?",
                "content": "Relays route traffic when direct peer-to-peer connections are blocked.",
                "type": "Concept",
                "detailed": "If two players have strict firewalls that prevent direct peer-to-peer connection, traffic routes seamlessly through community relay servers (similar to Steam networking or CNCNet tunnels)."
            },
            {
                "title": "Do relays cause lag or performance drops?",
                "content": "Typically no. Relays use high-bandwidth, low-latency backbone servers.",
                "type": "Concept",
                "detailed": "Relay servers are hosted on high-speed backbones and often provide comparable or better latency than congested direct peer-to-peer routes."
            },
            {
                "title": "How does the game select which relay to use?",
                "content": "Relay connections are formed dynamically on a player-to-player basis.",
                "type": "Feature",
                "detailed": "Relay connections are established dynamically per player pair, selecting the server location with the lowest latency for that match. Users in the same lobby can connect through different regional edge nodes to achieve optimal ping."
            },
            {
                "title": "Are relays secure?",
                "content": "Yes. Relays cannot decrypt match traffic.",
                "type": "Feature",
                "detailed": "Relay servers forward encrypted packets and do not have access to the encryption keys required to read or inspect traffic."
            },
            {
                "title": "Can I host a relay?",
                "content": "Community relay hosting is not needed at this time.",
                "type": "Concept",
                "detailed": "Generals Online operates on global edge infrastructure spanning hundreds of data centers worldwide, delivering low latency without requiring community relay hosting."
            }
        ]
    }
};


    function renderDemoContainer(secId) {
        if (secId === 'profiles') {
            return `
                <div class="gh-demo-wrapper" style="margin-bottom: 24px; padding: 20px; background: rgba(139, 92, 246, 0.05); border: 1px solid rgba(139, 92, 246, 0.25); border-radius: 12px;">
                    <div style="font-size: 16px; font-weight: 700; color: #ffffff; margin-bottom: 4px;">Demo: Profile Card</div>
                    <div style="font-size: 12px; color: #94a3b8; margin-bottom: 14px;">Basic interaction reference. Try hovering or clicking the action buttons!</div>
                    <div style="display: flex; justify-content: center;">
                        <div class="gh-profile-card" style="width: 280px; height: 360px; margin: 0 auto;" data-name="Demo Profile">
                            <img src="./assets/images/zerohour-cover.png" alt="Cover" class="gh-card-bg">
                            <div class="gh-card-gradient"></div>
                            <div class="gh-card-actions-bar">
                                <button class="gh-card-act-btn steam-btn" title="Steam"><img src="./assets/icons/steam-icon.png" alt="Steam" class="gh-act-icon-img"></button>
                                <button class="gh-card-act-btn edit-btn" title="Settings"><svg viewBox="0 0 24 24" width="13" height="13" fill="currentColor"><use href="#gh-icon-edit"></use></svg></button>
                                <button class="gh-card-act-btn clone-btn" title="Clone"><svg viewBox="0 0 24 24" width="13" height="13" fill="currentColor"><use href="#gh-icon-clone"></use></svg></button>
                                <button class="gh-card-act-btn shortcut-btn" title="Pin"><svg viewBox="0 0 24 24" width="13" height="13" fill="currentColor"><use href="#gh-icon-shortcut"></use></svg></button>
                            </div>
                            <div class="gh-card-hover">
                                <button class="gh-launch-btn">
                                    <svg viewBox="0 0 24 24"><polygon points="5 3 19 12 5 21 5 3"></polygon></svg>
                                    <span class="launch-text">LAUNCH</span>
                                </button>
                            </div>
                            <div class="gh-card-meta">
                                <div class="gh-card-info-row">
                                    <div class="gh-card-badge-icon"><img src="./assets/icons/generalshub-icon.png" alt="Icon"></div>
                                    <div class="gh-card-texts">
                                        <div class="gh-card-title">ShockWave 1.201</div>
                                        <div class="gh-card-sub">TheSuperHackers Engine</div>
                                    </div>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
            `;
        }
        if (secId === 'shortcuts') {
            return `
                <div class="gh-demo-wrapper" style="margin-bottom: 24px; padding: 20px; background: rgba(139, 92, 246, 0.05); border: 1px solid rgba(139, 92, 246, 0.25); border-radius: 12px;">
                    <div style="font-size: 16px; font-weight: 700; color: #ffffff; margin-bottom: 4px;">Demo: Desktop Shortcuts</div>
                    <div style="font-size: 12px; color: #94a3b8; margin-bottom: 14px;">Shortcut creation flow directly from profile card actions.</div>
                    <div style="display: flex; align-items: center; justify-content: center; gap: 24px; flex-wrap: wrap;">
                        <div style="text-align: center;">
                            <div style="font-size: 12px; font-weight: 700; color: #a78bfa; margin-bottom: 6px;">1. Click Pin Icon</div>
                            <div style="padding: 10px; background: #1e1b4b; border: 1px solid #7c3aed; border-radius: 8px; display: inline-block;">
                                <svg viewBox="0 0 24 24" width="28" height="28" fill="#a78bfa"><path d="M16,12V4H17V2H7V4H8V12L6,14V16H11.2V22H12.8V16H18V14L16,12M8.8,14L10,12.8V4H14V12.8L15.2,14H8.8Z"/></svg>
                            </div>
                        </div>
                        <div style="font-size: 20px; color: #64748b;">➔</div>
                        <div style="text-align: center;">
                            <div style="font-size: 12px; font-weight: 700; color: #10b981; margin-bottom: 6px;">2. Created on Desktop</div>
                            <div style="width: 80px; height: 80px; background: rgba(15, 23, 42, 0.8); border: 1px dashed rgba(16, 185, 129, 0.5); border-radius: 8px; display: flex; flex-direction: column; align-items: center; justify-content: center; margin: 0 auto;">
                                <img src="./assets/icons/generalshub-icon.png" alt="Shortcut" style="width: 36px; height: 36px;">
                                <span style="font-size: 9px; color: #e2e8f0; margin-top: 4px;">Zero Hour.lnk</span>
                            </div>
                        </div>
                    </div>
                </div>
            `;
        }
        if (secId === 'steam') {
            return `
                <div class="gh-demo-wrapper" style="margin-bottom: 24px; padding: 20px; background: rgba(139, 92, 246, 0.05); border: 1px solid rgba(139, 92, 246, 0.25); border-radius: 12px;">
                    <div style="font-size: 16px; font-weight: 700; color: #ffffff; margin-bottom: 4px;">Demo: Steam Status</div>
                    <div style="font-size: 12px; color: #94a3b8; margin-bottom: 14px;">Toggle Steam Broadcast and Overlay synchronization.</div>
                    <div style="display: flex; justify-content: center; align-items: center; gap: 16px;">
                        <button class="gh-btn-primary" id="ghDemoSteamToggleBtn" style="display: flex; align-items: center; gap: 8px;">
                            <img src="./assets/icons/steam-icon.png" alt="Steam" style="width: 18px; height: 18px;">
                            <span>Steam Overlay Active (AppID: 24860)</span>
                        </button>
                    </div>
                </div>
            `;
        }
        if (secId === 'scangames') {
            return `
                <div class="gh-demo-wrapper">
                    <div style="font-size: 15px; font-weight: 700; color: #ffffff; margin-bottom: 4px;">Demo: Automatic Game Discovery</div>
                    <div style="font-size: 12px; color: #94a3b8; margin-bottom: 12px;">Scans registry keys, Steam libraries, and EA App directories.</div>
                    <div style="background: #090615; padding: 12px; border-radius: 8px; font-family: var(--font-mono); font-size: 11px; color: #34d399; overflow-x: auto; word-break: break-all; overflow-wrap: anywhere; line-height: 1.45;">
                        <div>✓ Steam Library: C:\\Program Files (x86)\\Steam\\steamapps\\common\\Command and Conquer Generals</div>
                        <div style="margin-top: 6px;">✓ EA App: C:\\Program Files\\EA Games\\Command and Conquer Generals Zero Hour</div>
                        <div style="margin-top: 6px; color: #94a3b8;">• Retail CD/DVD: Not found (Skipped)</div>
                    </div>
                </div>
            `;
        }
        if (secId === 'workspace' || secId === 'workspaces') {
            return `
                <div class="gh-demo-wrapper">
                    <div style="font-size: 15px; font-weight: 700; color: #ffffff; margin-bottom: 4px;">Demo: Virtual Workspace (NTFS Hardlinks)</div>
                    <div style="font-size: 12px; color: #94a3b8; margin-bottom: 12px;">Instant profile launching with 0 extra disk duplication.</div>
                    <div class="gh-workspace-demo-grid">
                        <div style="padding: 12px; background: rgba(239, 68, 68, 0.1); border: 1px solid rgba(239, 68, 68, 0.3); border-radius: 8px;">
                            <strong style="color: #f87171; font-size: 12px;">Traditional Mod Copy:</strong>
                            <div style="font-size: 11px; color: #cbd5e1; margin-top: 4px; line-height: 1.4;">Duplicates 10 GB per mod installation. 5 mods = 50 GB. Slow copy times.</div>
                        </div>
                        <div style="padding: 12px; background: rgba(16, 185, 129, 0.1); border: 1px solid rgba(16, 185, 129, 0.3); border-radius: 8px;">
                            <strong style="color: #34d399; font-size: 12px;">GenHub Hardlinks:</strong>
                            <div style="font-size: 11px; color: #cbd5e1; margin-top: 4px; line-height: 1.4;">Links to clean base files in 0.02s. 5 mods = 0 B extra disk space. Pristine core files.</div>
                        </div>
                    </div>
                </div>
            `;
        }
        return '';
    }

    function formatMarkdown(md) {
        if (!md) return '';
        let html = md
            .replace(/\*\*(.*?)\*\*/g, '<strong>$1</strong>')
            .replace(/\*(\S[^*]*?)\*/g, '<em>$1</em>')
            .replace(/^\s*[\*\-]\s+(.*)$/gm, '<li>$1</li>');
        html = html.replace(/((?:<li>.*?<\/li>\s*)+)/g, '<ul style="margin: 8px 0; padding-left: 20px;">$1</ul>');
        return html.replace(/\n/g, '<br>');
    }

    function renderInfoSection(secKey) {
        // Support section aliases
        if (secKey === 'workspace' && infoData['workspaces']) secKey = 'workspaces';
        if (secKey === 'faq' && infoData['gofaq']) secKey = 'gofaq';
        const section = infoData[secKey] || infoData['quickstart'];
        const titleEl = document.getElementById('ghInfoSectionTitle');
        const descEl = document.getElementById('ghInfoSectionDesc');
        const container = document.getElementById('ghInfoCardsContainer');
        const contentArea = document.getElementById('ghInfoContentArea');

        if (titleEl) titleEl.textContent = section.title;
        if (descEl) descEl.textContent = section.desc;
        if (contentArea) contentArea.scrollTop = 0;

        if (container) {
            const demoHtml = renderDemoContainer(secKey);
            const cardsHtml = (section.cards || []).map(c => `
                <div class="gh-info-card">
                    <div class="gh-info-card-header">
                        <h4>${c.title}</h4>
                        <span class="gh-chip">${c.type || 'Concept'}</span>
                    </div>
                    <p style="font-size: 13.5px; color: #cbd5e1; line-height: 1.5; margin: 8px 0;">${c.content}</p>
                    ${c.detailed ? `<button class="gh-info-expand-btn">Show Details ▾</button>
                    <div class="gh-info-detailed-content" style="font-size: 13px; color: #94a3b8; line-height: 1.6; padding-top: 10px;">
                        ${formatMarkdown(c.detailed)}
                    </div>` : ''}
                </div>
            `).join('');

            container.innerHTML = demoHtml + cardsHtml;

            // Wire expand buttons
            container.querySelectorAll('.gh-info-expand-btn').forEach(b => {
                b.addEventListener('click', () => {
                    const content = b.nextElementSibling;
                    if (content) {
                        const isShown = content.classList.toggle('active');
                        b.textContent = isShown ? 'Hide Details ▴' : 'Show Details ▾';
                    }
                });
            });

            // Wire Demo Profile Card launch / action buttons inside Demo container
            const demoCard = container.querySelector('.gh-profile-card');
            if (demoCard) {
                wireProfileCard(demoCard);
            }
        }
    }

    infoNavBtns.forEach(btn => {
        btn.addEventListener('click', () => {
            const id = btn.getAttribute('data-info-id');
            infoNavBtns.forEach(b => b.classList.remove('active'));
            btn.classList.add('active');
            renderInfoSection(id);
            if (typeof btn.scrollIntoView === 'function') {
                btn.scrollIntoView({ behavior: 'smooth', block: 'nearest', inline: 'nearest' });
            }
        });
    });

    // Module Selector Dropdown (GenHub Guides, Zero Hour FAQ, Changelogs)
    const infoModuleSelect = document.getElementById('ghInfoModuleSelect');
    if (infoModuleSelect) {
        infoModuleSelect.addEventListener('change', (e) => {
            const val = e.target.value;
            const navList = document.getElementById('ghInfoNavList');

            if (val === 'guide') {
                if (navList) {
                    navList.innerHTML = `
                        <button class="gh-info-nav-btn active" data-info-id="quickstart">Quickstart Guide</button>
                        <button class="gh-info-nav-btn" data-info-id="profiles">Game Profiles</button>
                        <button class="gh-info-nav-btn" data-info-id="settings">Game Settings</button>
                        <button class="gh-info-nav-btn" data-info-id="content">Profile Content</button>
                        <button class="gh-info-nav-btn" data-info-id="shortcuts">Desktop Shortcuts</button>
                        <button class="gh-info-nav-btn" data-info-id="steam">Steam Integration</button>
                        <button class="gh-info-nav-btn" data-info-id="local">Local Content</button>
                        <button class="gh-info-nav-btn" data-info-id="tools">Tools & Utilities</button>
                        <button class="gh-info-nav-btn" data-info-id="gofaq">Generals Online FAQ</button>
                        <button class="gh-info-nav-btn" data-info-id="gochange">Generals Online Changelog</button>
                        <button class="gh-info-nav-btn" data-info-id="scangames">Game Detection</button>
                        <button class="gh-info-nav-btn" data-info-id="workspaces">Virtual Workspaces</button>
                        <button class="gh-info-nav-btn" data-info-id="appupdates">App Updates</button>
                        <button class="gh-info-nav-btn" data-info-id="changelog">Changelog</button>
                    `;
                    navList.querySelectorAll('.gh-info-nav-btn').forEach(b => {
                        b.addEventListener('click', () => {
                            navList.querySelectorAll('.gh-info-nav-btn').forEach(x => x.classList.remove('active'));
                            b.classList.add('active');
                            renderInfoSection(b.getAttribute('data-info-id'));
                            if (typeof b.scrollIntoView === 'function') {
                                b.scrollIntoView({ behavior: 'smooth', block: 'nearest', inline: 'nearest' });
                            }
                        });
                    });
                }
                renderInfoSection('quickstart');
            } else if (val === 'faq') {
                if (navList) {
                    navList.innerHTML = `
                        <button class="gh-info-nav-btn active" data-info-id="zh_problems_game">Problems with the Game</button>
                        <button class="gh-info-nav-btn" data-info-id="zh_problems_multiplayer">Problems with Multiplayer</button>
                        <button class="gh-info-nav-btn" data-info-id="zh_general_faq">Frequently Asked Questions</button>
                    `;
                    navList.querySelectorAll('.gh-info-nav-btn').forEach(b => {
                        b.addEventListener('click', () => {
                            navList.querySelectorAll('.gh-info-nav-btn').forEach(x => x.classList.remove('active'));
                            b.classList.add('active');
                            renderInfoSection(b.getAttribute('data-info-id'));
                            if (typeof b.scrollIntoView === 'function') {
                                b.scrollIntoView({ behavior: 'smooth', block: 'nearest', inline: 'nearest' });
                            }
                        });
                    });
                }
                renderInfoSection('zh_problems_game');
            } else if (val === 'changelogs') {
                if (navList) {
                    navList.innerHTML = `
                        <button class="gh-info-nav-btn active" data-info-id="changelog">GenHub Changelog</button>
                        <button class="gh-info-nav-btn" data-info-id="gochange">Generals Online Changelog</button>
                    `;
                    navList.querySelectorAll('.gh-info-nav-btn').forEach(b => {
                        b.addEventListener('click', () => {
                            navList.querySelectorAll('.gh-info-nav-btn').forEach(x => x.classList.remove('active'));
                            b.classList.add('active');
                            renderInfoSection(b.getAttribute('data-info-id'));
                            if (typeof b.scrollIntoView === 'function') {
                                b.scrollIntoView({ behavior: 'smooth', block: 'nearest', inline: 'nearest' });
                            }
                        });
                    });
                }
                renderInfoSection('changelog');
            }
        });
    }

    // Initial render of Quickstart Guide
    renderInfoSection('quickstart');

    // Delegated listener for dynamically rendered demo buttons & expanders
    document.addEventListener('click', (e) => {
        const steamBtn = e.target.closest('#ghDemoSteamToggleBtn');
        if (steamBtn) {
            const isSteamActive = steamBtn.classList.toggle('steam-off');
            const span = steamBtn.querySelector('span');
            if (span) {
                span.textContent = isSteamActive 
                    ? 'Steam Integration Paused (Standby)' 
                    : 'Steam Overlay Active (AppID: 24860)';
            }
            window.showGenHubToast(
                isSteamActive ? 'Warning' : 'Success',
                'Steam Status',
                isSteamActive ? 'Steam overlay disconnected.' : 'Steam overlay re-synchronized.'
            );
        }
    });

})();
