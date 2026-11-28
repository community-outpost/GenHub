"use strict";
/**
 * GenHub Native Desktop Client Simulation (Avalonia XAML Engine Parity)
 * 1:1 Flow, Modals, State Handling, Notifications, Diagnostics
 */
(function() {
    /**
     * Escapes a value for safe interpolation into HTML markup.
     * @param {*} str Value to escape.
     * @returns {string} The escaped text, or an empty string for falsy input.
     */
    function escapeHtml(str) {
        if (!str) return '';
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    /**
     * Shows a dismissible toast notification inside the simulated client.
     * @param {string} type Toast kind such as Success, Warning or Info.
     * @param {string} title Toast heading.
     * @param {string} desc Toast body text.
     */
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

    /**
     * Closes every open modal backdrop when Escape is pressed.
     * @param {KeyboardEvent} e Keydown event.
     */
    function handleEscapeKey(e) {
        if (e.key === 'Escape') {
            document.querySelectorAll('.gh-modal-backdrop.open').forEach(modal => {
                closeModal(modal.id);
            });
        }
    }

    /**
     * Opens the modal with the given element id.
     * @param {string} id Modal element id.
     */
    function openModal(id) {
        const modal = document.getElementById(id);
        if (modal) {
            modal.classList.add('active');
        }
    }

    /**
     * Closes the modal with the given element id.
     * @param {string} id Modal element id.
     */
    function closeModal(id) {
        const modal = document.getElementById(id);
        if (modal) {
            modal.classList.remove('active');
        }
    }

    /**
     * Wires the Escape key, explicit close buttons and backdrop clicks of every modal.
     */
    function initModals() {
        document.addEventListener('keydown', handleEscapeKey);
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
    }

    initModals();

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
    const paneStatusMessages = new Map([
        ['profiles', 'Ready • Workspaces Synced (NTFS Hardlinks Active)'],
        ['downloads', 'Downloads Browser • Connected to Community Catalog'],
        ['tools', 'Tools Suite • Replay Analyzer, Map Manager & Hotkeys Editor Active'],
        ['settings', 'Configuration Loaded (~/.config/GenHub/settings.json)'],
        ['info', 'Documentation & Frequently Asked Questions']
    ]);

    /**
     * Activates the given main navigation pane and syncs the titlebar and status bar.
     * @param {string} paneKey Pane key from a pill button's data-pane attribute.
     */
    function switchViewPane(paneKey) {
        pillButtons.forEach(btn => {
            const isTarget = btn.getAttribute('data-pane') === paneKey;
            btn.classList.toggle('active', isTarget);
            btn.setAttribute('aria-selected', isTarget ? 'true' : 'false');
        });

        updateTitlebarForPane(paneKey);

        Object.keys(panes).forEach(k => {
            if (panes[k]) {
                panes[k].classList.toggle('active', k === paneKey);
            }
        });

        updateStatusForPane(paneKey);
    }

    /**
     * Hides the notification flyout and highlights the titlebar button for the pane.
     * @param {string} paneKey Active pane key.
     */
    function updateTitlebarForPane(paneKey) {
            const notifFlyout = document.getElementById('ghNotificationFlyout');
            if (notifFlyout) notifFlyout.classList.remove('active');

            // Toggle titlebar buttons active states
            const infoBtn = document.getElementById('ghTbBtnInfo');
            const settingsBtn = document.getElementById('ghTbBtnSettings');
            if (infoBtn) infoBtn.classList.toggle('active', paneKey === 'info');
            if (settingsBtn) settingsBtn.classList.toggle('active', paneKey === 'settings');
    }

    /**
     * Updates the status bar text for the pane and runs pane-specific refreshes.
     * @param {string} paneKey Active pane key.
     */
    function updateStatusForPane(paneKey) {
        const statusText = document.getElementById('ghStatusText');
        const statusMessage = paneStatusMessages.get(paneKey);
        if (!statusText || statusMessage === undefined) return;
        statusText.textContent = statusMessage;
        if (paneKey === 'tools') {
            syncActiveToolPane();
        } else if (paneKey === 'info') {
            resetInfoContentScroll();
        }
    }

    /**
     * Highlights the remembered tool in the Tools sidebar and shows its subpane.
     */
    function syncActiveToolPane() {
        document.querySelectorAll('.gh-tool-item').forEach(b => {
            b.classList.toggle('active', b.getAttribute('data-tool') === activeToolKey);
        });
        document.querySelectorAll('.gh-tool-subpane').forEach(pane => {
            pane.classList.toggle('active', pane.id === `tool-pane-${activeToolKey}`);
        });
    }

    /**
     * Scrolls the Info content area back to the top.
     */
    function resetInfoContentScroll() {
        const infoContent = document.getElementById('ghInfoContentArea');
        if (infoContent) infoContent.scrollTop = 0;
    }

    /**
     * Wires the pill navigation buttons and the Info and Settings titlebar buttons.
     */
    function initViewNavigation() {
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
    }

    /**
     * Finds the notification badge element under any of its known ids.
     * @returns {HTMLElement|null} The badge element, if present.
     */
    function findNotificationBadge() {
        return document.getElementById('ghBellBadge') || document.getElementById('ghTbBellBadge') || document.getElementById('ghNotifBadge');
    }

    /**
     * Wires the notification bell, flyout, clear and mute buttons.
     */
    function initNotifications() {
        const notifTbBtn = document.getElementById('ghTbBtnBell') || document.getElementById('ghTbBtnNotifications');
        const notifFlyout = document.getElementById('ghNotificationFlyout');
        const notifBadge = findNotificationBadge();
        const notifFlyoutBadge = document.getElementById('ghNotifFlyoutBadge');

        if (notifTbBtn && notifFlyout) {
            wireNotificationFlyout(notifTbBtn, notifFlyout, notifBadge, notifFlyoutBadge);
        }

        wireClearNotificationsButton(notifBadge, notifFlyoutBadge);
        wireMuteNotificationsButton();
    }

    /**
     * Toggles the notification flyout from the bell and closes it on outside clicks.
     * @param {HTMLElement} notifTbBtn Bell button.
     * @param {HTMLElement} notifFlyout Flyout panel.
     * @param {HTMLElement|null} notifBadge Unread badge on the bell.
     * @param {HTMLElement|null} notifFlyoutBadge Unread counter inside the flyout.
     */
    function wireNotificationFlyout(notifTbBtn, notifFlyout, notifBadge, notifFlyoutBadge) {
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

    /**
     * Wires the button that clears the notification feed.
     * @param {HTMLElement|null} notifBadge Unread badge on the bell.
     * @param {HTMLElement|null} notifFlyoutBadge Unread counter inside the flyout.
     */
    function wireClearNotificationsButton(notifBadge, notifFlyoutBadge) {
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
    }

    /**
     * Wires the button that toggles the notification mute indicator.
     */
    function wireMuteNotificationsButton() {
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
    }

    /**
     * Wires the simulated window close buttons.
     */
    function initWindowControls() {
        document.querySelectorAll('.gh-win-btn.close').forEach(btn => {
            btn.addEventListener('click', () => {
                window.showGenHubToast('Warning', 'Close Attempt', 'Desktop client minimizes to system tray when closed.');
            });
        });
    }

    initViewNavigation();
    initNotifications();
    initWindowControls();

    // Profile Card State and Context Management
    let activeCardEditing = null;
    let isCreatingProfile = false;

    /**
     * Wires a profile card's launch and action buttons.
     * @param {HTMLElement} card Profile card element.
     */
    function wireProfileCard(card) {
        bindProfileCardButton(card, '.gh-launch-btn', toggleProfileLaunch);
        bindProfileCardButton(card, '.share-btn', openShareProfileDialog);
        bindProfileCardButton(card, '.steam-btn', announceSteamIntegration);
        bindProfileCardButton(card, '.edit-btn', openProfileSettingsForCard);
        bindProfileCardButton(card, '.clone-btn', cloneProfileCard);
        bindProfileCardButton(card, '.shortcut-btn', announceProfileShortcut);
        bindProfileCardButton(card, '.delete-btn', deleteProfileCard);
    }

    /**
     * Binds a click handler to a card button that stops propagation before running.
     * @param {HTMLElement} card Profile card element.
     * @param {string} selector Selector of the button inside the card.
     * @param {function(HTMLElement, HTMLElement): void} handler Receives the card and the button.
     */
    function bindProfileCardButton(card, selector, handler) {
        const button = card.querySelector(selector);
        if (button) {
            button.addEventListener('click', (e) => {
                e.stopPropagation();
                handler(card, button);
            });
        }
    }

    /**
     * Starts or stops the simulated game process for a profile card.
     * @param {HTMLElement} card Profile card element.
     * @param {HTMLElement} launchBtn The card's launch button.
     */
    function toggleProfileLaunch(card, launchBtn) {
        const pName = card.getAttribute('data-name') || 'Zero Hour';
        const isRunning = card.classList.contains('active-profile');
        const textSpan = launchBtn.querySelector('.launch-text');

        if (!isRunning) {
            startProfileProcess(card, textSpan, pName);
        } else {
            stopProfileProcess(card, textSpan, pName);
        }
    }

    /**
     * Marks a card as the only running profile and announces the launch.
     * @param {HTMLElement} card Profile card element.
     * @param {HTMLElement|null} textSpan Launch button label.
     * @param {string} pName Profile name.
     */
    function startProfileProcess(card, textSpan, pName) {
        document.querySelectorAll('.gh-profile-card').forEach(c => {
            c.classList.remove('active-profile');
            const otherBtn = c.querySelector('.launch-text');
            if (otherBtn) otherBtn.textContent = 'LAUNCH';
        });
        card.classList.add('active-profile');
        if (!card.querySelector('.gh-running-badge')) {
            addRunningBadge(card);
        }
        if (textSpan) textSpan.textContent = 'STOP';
        const statusText = document.getElementById('ghStatusText');
        if (statusText) statusText.textContent = `Running: ${pName} (PID: 14820)`;
        window.showGenHubToast('Success', 'Game Launched', `Process started for "${pName}". DirectDraw and GenTool injected.`);
    }

    /**
     * Inserts the RUNNING badge into a profile card.
     * @param {HTMLElement} card Profile card element.
     */
    function addRunningBadge(card) {
        const badge = document.createElement('div');
        badge.className = 'gh-running-badge';
        badge.innerHTML = '<span class="gh-running-dot"></span> RUNNING';
        const grad = card.querySelector('.gh-card-gradient');
        if (grad) grad.after(badge);
        else card.prepend(badge);
    }

    /**
     * Marks a card as stopped and announces the process exit.
     * @param {HTMLElement} card Profile card element.
     * @param {HTMLElement|null} textSpan Launch button label.
     * @param {string} pName Profile name.
     */
    function stopProfileProcess(card, textSpan, pName) {
        card.classList.remove('active-profile');
        if (textSpan) textSpan.textContent = 'LAUNCH';
        const statusText = document.getElementById('ghStatusText');
        if (statusText) statusText.textContent = 'Ready • Workspaces Synced (NTFS Hardlinks Active)';
        window.showGenHubToast('Info', 'Process Stopped', `Closed "${pName}". Hardlink workspace intact.`);
    }

    /**
     * Opens the share dialog populated with a profile's name, cover and genhub:// link.
     * @param {HTMLElement} card Profile card element.
     */
    function openShareProfileDialog(card) {
        const pName = card.getAttribute('data-name') || 'Zero Hour';
        const shareUriInput = document.getElementById('ghShareUriInput');
        populateShareDialogHeader(card, pName);

        const dummyToken = btoa(unescape(encodeURIComponent(JSON.stringify({
            name: pName,
            version: '1.04',
            created: new Date().toISOString()
        })))).substring(0, 32);
        if (shareUriInput) shareUriInput.value = `genhub://profile/import?data=${dummyToken}`;

        openModal('ghShareProfileDialogModal');
    }

    /**
     * Copies a profile's name and cover image into the share dialog header.
     * @param {HTMLElement} card Profile card element.
     * @param {string} pName Profile name.
     */
    function populateShareDialogHeader(card, pName) {
        const shareTitle = document.getElementById('ghShareProfileName');
        const shareCover = document.getElementById('ghShareProfileCover');
        const cardImg = card.querySelector('.gh-card-bg');

        if (shareTitle) shareTitle.textContent = pName;
        if (shareCover && cardImg) shareCover.src = cardImg.src;
    }

    /**
     * Announces that a profile is connected to the Steam overlay.
     * @param {HTMLElement} card Profile card element.
     */
    function announceSteamIntegration(card) {
        const pName = card.getAttribute('data-name') || 'Profile';
        window.showGenHubToast('Info', 'Steam Integration', `Connected "${pName}" with Steam Overlay & Friends Broadcast (AppID: 24860).`);
    }

    /**
     * Opens the profile settings window to edit an existing card.
     * @param {HTMLElement} card Profile card element.
     */
    function openProfileSettingsForCard(card) {
        activeCardEditing = card;
        isCreatingProfile = false;
        const pName = card.getAttribute('data-name') || 'Shockwave 1.201';
        fillProfileEditorHeader(pName, pName);

        if (typeof selectWinTab === 'function') selectWinTab(1);
        if (typeof selectSubCategory === 'function') selectSubCategory('identity');
        openModal('ghGameProfileSettingsWindow');
    }

    /**
     * Sets the profile settings window title and the profile name input.
     * @param {string} titleText Window title text.
     * @param {string} nameValue Profile name input value.
     */
    function fillProfileEditorHeader(titleText, nameValue) {
        const titleEl = document.getElementById('ghWinProfileTitle');
        const nameInput = document.getElementById('ghProfileNameInput');
        if (titleEl) titleEl.textContent = titleText;
        if (nameInput) nameInput.value = nameValue;
    }

    /**
     * Duplicates a profile card into the profile list.
     * @param {HTMLElement} card Profile card element.
     */
    function cloneProfileCard(card) {
        const pName = card.getAttribute('data-name') || 'Profile';
        const clone = card.cloneNode(true);
        clone.setAttribute('data-name', `${pName} (Copy)`);
        clone.classList.remove('active-profile');
        const titleEl = clone.querySelector('.gh-card-title');
        if (titleEl) titleEl.textContent = `${pName} (Copy)`;

        resetClonedLaunchButton(clone);

        wireProfileCard(clone);
        const list = document.getElementById('ghProfilesList');
        const addCard = document.getElementById('ghAddNewProfileBtn');
        list.insertBefore(clone, addCard);
        updateProfilesCount();
        window.showGenHubToast('Success', 'Profile Cloned', `Created isolated copy: "${pName} (Copy)".`);
    }

    /**
     * Resets the launch button of a cloned card to its idle state.
     * @param {HTMLElement} clone Cloned profile card.
     */
    function resetClonedLaunchButton(clone) {
        const cloneLaunch = clone.querySelector('.gh-launch-btn');
        if (cloneLaunch) {
            cloneLaunch.classList.remove('running');
            cloneLaunch.style.background = '';
            const launchText = cloneLaunch.querySelector('.launch-text') || cloneLaunch;
            if (launchText) launchText.textContent = 'LAUNCH';
        }
    }

    /**
     * Announces a desktop shortcut for a profile.
     * @param {HTMLElement} card Profile card element.
     */
    function announceProfileShortcut(card) {
        const pName = card.getAttribute('data-name') || 'Profile';
        window.showGenHubToast('Success', 'Shortcut Created', `Added desktop launcher icon for "${pName}".`);
    }

    /**
     * Removes a profile card from the list.
     * @param {HTMLElement} card Profile card element.
     */
    function deleteProfileCard(card) {
        const pName = card.getAttribute('data-name') || 'Profile';
        card.remove();
        updateProfilesCount();
        window.showGenHubToast('Warning', 'Profile Removed', `Deleted "${pName}" workspace from disk.`);
    }

    document.querySelectorAll('.gh-profile-card:not(.add-card)').forEach(wireProfileCard);

    /**
     * Refreshes the loaded profile counter.
     */
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

    /**
     * Shows the profile settings window tab at the given index.
     * @param {number} idx Tab index.
     */
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

    /**
     * Shows the profile settings sub-category panel with the given key.
     * @param {string} cat Sub-category key.
     */
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

    const addProfileBtn = document.getElementById('ghAddNewProfileBtn');

    /**
     * Wires the add profile card, accent randomizer and fullscreen toggle.
     */
    function initProfileEditorActions() {
        if (addProfileBtn) {
            addProfileBtn.addEventListener('click', () => {
                activeCardEditing = null;
                isCreatingProfile = true;
                fillProfileEditorHeader('New Profile', 'Custom Profile');
                selectWinTab(1);
                selectSubCategory('identity');
                openModal('ghGameProfileSettingsWindow');
            });
        }

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
    }

    /**
     * Wires the save button and the add local content button of the profile settings window.
     */
    function initProfileEditorSave() {
        const saveFabBtn = document.getElementById('ghSaveProfileFabBtn');
        if (saveFabBtn) {
            saveFabBtn.addEventListener('click', saveProfileFromEditor);
        }

        const addLocalBtn = document.getElementById('ghWinAddLocalContentBtn');
        if (addLocalBtn) {
            addLocalBtn.addEventListener('click', () => {
                window.showGenHubToast('Info', 'Local Content', 'Select a folder or BIG archive to link into this profile workspace.');
            });
        }
    }

    /**
     * Creates or updates a profile from the settings window and closes it.
     */
    function saveProfileFromEditor() {
        const name = readProfileEditorName();

        if (isCreatingProfile) {
            addCreatedProfileCard(name);
        } else if (activeCardEditing) {
            updateEditedProfileCard(name);
        }

        closeModal('ghGameProfileSettingsWindow');
    }

    /**
     * Reads the profile name from the settings window.
     * @returns {string} The trimmed name, or Custom Profile when empty.
     */
    function readProfileEditorName() {
        const nameInput = document.getElementById('ghProfileNameInput');
        return nameInput ? nameInput.value.trim() || 'Custom Profile' : 'Custom Profile';
    }

    /**
     * Adds a new profile card built from the settings window.
     * @param {string} name Profile name.
     */
    function addCreatedProfileCard(name) {
        const newCard = document.createElement('div');
        newCard.className = 'gh-profile-card';
        newCard.setAttribute('data-name', name);
        newCard.innerHTML = `
                    <img src="./assets/images/zerohour-cover.png" alt="Profile Cover" class="gh-card-bg">
                    <div class="gh-card-gradient"></div>
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
    }

    /**
     * Applies the edited name to the card being edited.
     * @param {string} name Profile name.
     */
    function updateEditedProfileCard(name) {
        activeCardEditing.setAttribute('data-name', name);
        const titleEl = activeCardEditing.querySelector('.gh-card-title');
        if (titleEl) titleEl.textContent = name;
        window.showGenHubToast('Success', 'Profile Saved', `Settings saved for "${name}".`);
    }

    initProfileEditorActions();
    initProfileEditorSave();

    // -------------------------------------------------------------
    // PR #400: PROFILE SHARING & RICH IMPORT INSPECTION
    // -------------------------------------------------------------
    /**
     * Wires the share link copy, package export and profile import buttons.
     */
    function initProfileSharing() {
        const copyShareLinkBtn = document.getElementById('ghCopyShareLinkBtn');
        if (copyShareLinkBtn) {
            copyShareLinkBtn.addEventListener('click', () => {
                const shareUriInput = document.getElementById('ghShareUriInput');
                const link = shareUriInput ? shareUriInput.value : 'genhub://profile/import?...';
                if (navigator.clipboard?.writeText) {
                    navigator.clipboard.writeText(link).catch(() => { /* ignore clipboard errors */ });
                }
                const orig = copyShareLinkBtn.textContent;
                copyShareLinkBtn.textContent = 'Copied!';
                setTimeout(() => { copyShareLinkBtn.textContent = orig; }, 1500);
                window.showGenHubToast('Success', 'Share Link Copied', 'Protocol link copied! Paste into Discord or browser to share.');
            });
        }

        // Export .ghprofile Package Button
        const exportGhProfileBtn = document.getElementById('ghExportGhProfileBtn');
        if (exportGhProfileBtn) {
            exportGhProfileBtn.addEventListener('click', () => {
                const shareTitle = document.getElementById('ghShareProfileName')?.textContent || 'Profile';
                const safeName = shareTitle.replace(/[^a-zA-Z0-9_-]/g, '_');
                window.showGenHubToast('Success', 'Package Exported', `Saved "${safeName}.ghprofile" package to Downloads folder.`);
                closeModal('ghShareProfileDialogModal');
            });
        }

        // Header Import Button
        const importProfileBtn = document.getElementById('ghImportProfileBtn');
        if (importProfileBtn) {
            importProfileBtn.addEventListener('click', () => {
                openModal('ghImportProfileInspectionModal');
            });
        }

        const confirmImportProfileBtn = document.getElementById('ghConfirmImportProfileBtn');
        if (confirmImportProfileBtn) {
            confirmImportProfileBtn.addEventListener('click', confirmProfileImport);
        }
    }

    /**
     * Adds the inspected shared profile to the profile list.
     */
    function confirmProfileImport() {
        const importedName = readImportedProfileName();

        const list = document.getElementById('ghProfilesList');
        const addNewBtn = document.getElementById('ghAddNewProfileBtn');
        const newCard = createImportedProfileCard(importedName);
        wireProfileCard(newCard);
        if (list && addNewBtn) {
            list.insertBefore(newCard, addNewBtn);
        }
        updateProfilesCount();
        closeModal('ghImportProfileInspectionModal');
        window.showGenHubToast('Success', 'Profile Imported', `Successfully imported "${importedName}" with 3 verified manifests!`);
    }

    /**
     * Reads the profile name from the import inspection dialog.
     * @returns {string} The trimmed name, or the default tournament profile name.
     */
    function readImportedProfileName() {
        const nameInput = document.getElementById('ghImportProfileNameInput');
        return nameInput?.value?.trim() || 'Tournament Zero Hour v1.04';
    }

    /**
     * Builds the profile card for an imported shared profile.
     * @param {string} importedName Profile name.
     * @returns {HTMLDivElement} The new, unwired card.
     */
    function createImportedProfileCard(importedName) {
        const newCard = document.createElement('div');
        newCard.className = 'gh-profile-card';
        newCard.setAttribute('data-profile-id', `p_${Date.now()}`);
        newCard.setAttribute('data-name', importedName);
        newCard.innerHTML = `
                <img src="./assets/images/zerohour-cover.png" alt="${escapeHtml(importedName)} Cover" class="gh-card-bg">
                <div class="gh-card-gradient"></div>
                <div class="gh-card-actions-bar">
                    <button class="gh-card-act-btn share-btn" title="Share Profile (genhub://)">
                        <svg viewBox="0 0 24 24" width="13" height="13" fill="currentColor"><use href="#gh-icon-share"></use></svg>
                    </button>
                    <button class="gh-card-act-btn steam-btn" title="Steam Overlay & Playtime Tracking">
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
                            <img src="./assets/images/communityoutpost-logo.png" alt="Community Outpost">
                        </div>
                        <div class="gh-card-texts">
                            <div class="gh-card-title">${escapeHtml(importedName)}</div>
                            <div class="gh-card-sub">Imported from shared genhub:// link</div>
                        </div>
                    </div>
                    <div class="gh-card-tags">
                        <span class="gh-tag">v1.04</span>
                        <span class="gh-tag">Shared Profile</span>
                    </div>
                </div>
            `;
        return newCard;
    }

    /**
     * Wires the game installation scan button.
     */
    function initScanButton() {
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
    }

    initProfileSharing();
    initScanButton();

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

    /**
     * Shows only the catalog cards that match the publisher, search text and category.
     */
    function filterDownloadsCatalog() {
        const searchFilterQuery = readCatalogSearchQuery();
        const cat = document.getElementById('ghCatalogFilter')?.value || 'all';

        document.querySelectorAll('#ghCatalogGrid .gh-content-card').forEach(card => {
            card.style.display = catalogCardMatches(readCatalogCard(card), searchFilterQuery, cat) ? '' : 'none';
        });
    }

    /**
     * Reads the catalog search text, reducing a ModDB URL to its slug (PR #481).
     * @returns {string} Lowercase search query.
     */
    function readCatalogSearchQuery() {
        const searchFilterQuery = (document.getElementById('ghCatalogSearch')?.value || '').toLowerCase().trim();
        if (!searchFilterQuery.includes('moddb.com/')) {
            return searchFilterQuery;
        }
        const cleanUrl = searchFilterQuery.split('?')[0].replace(/\/+$/, '');
        const slug = cleanUrl.split('/').pop();
        return slug ? slug.replace(/[-_]/g, ' ') : searchFilterQuery;
    }

    /**
     * Reads the filterable attributes of a catalog card.
     * @param {HTMLElement} card Catalog card element.
     * @returns {{pub: string, title: string, category: string}} Card publisher, lowercase title and category.
     */
    function readCatalogCard(card) {
        return {
            pub: card.getAttribute('data-pub') || '',
            title: (card.getAttribute('data-title') || '').toLowerCase(),
            category: card.getAttribute('data-category') || ''
        };
    }

    /**
     * Tests a catalog card against the active publisher, search query and category.
     * @param {{pub: string, title: string, category: string}} cardInfo Card attributes.
     * @param {string} query Lowercase search query.
     * @param {string} cat Selected category, or all.
     * @returns {boolean} True when the card should be shown.
     */
    function catalogCardMatches(cardInfo, query, cat) {
        const matchesQuery = !query || cardInfo.title.includes(query);
        const matchesCat = cat === 'all' || cardInfo.category === cat;
        return cardInfo.pub === activePublisherKey && matchesQuery && matchesCat;
    }

    /**
     * Switches the downloads catalog to the publisher of the clicked button.
     * @param {HTMLElement} btn Publisher button.
     * @param {NodeListOf<HTMLElement>} pubBtns All publisher buttons.
     */
    function selectPublisher(btn, pubBtns) {
        const key = btn.getAttribute('data-pub');
        activePublisherKey = key;

        pubBtns.forEach(b => b.classList.remove('active'));
        btn.classList.add('active');

        updatePublisherHeader(key);
        updateCatalogFilterOptions(key);
        showDownloadsBrowserView();

        filterDownloadsCatalog();
        window.showGenHubToast('Info', 'Switched Publisher', `Viewing catalog from ${describePublisher(key)}`);
    }

    /**
     * Returns the display title of a publisher.
     * @param {string} key Publisher key.
     * @returns {string} Publisher title, or the key when unknown.
     */
    function describePublisher(key) {
        return pubMeta[key]?.title || key;
    }

    /**
     * Shows the publisher title and description above the catalog.
     * @param {string} key Publisher key.
     */
    function updatePublisherHeader(key) {
        if (pubMeta[key]) {
            const tEl = document.getElementById('ghActivePubTitle');
            const dEl = document.getElementById('ghActivePubDesc');
            if (tEl) tEl.textContent = pubMeta[key].title;
            if (dEl) dEl.textContent = pubMeta[key].desc;
        }
    }

    /**
     * Updates the category filter options for the publisher (PR #481 ModDB sections).
     * @param {string} key Publisher key.
     */
    function updateCatalogFilterOptions(key) {
        const cFilter = document.getElementById('ghCatalogFilter');
        if (!cFilter) return;
        if (key === 'moddb') {
            cFilter.innerHTML = `
                        <option value="all">All Sections</option>
                        <option value="mod">Mods (Total Conversions)</option>
                        <option value="addon">Addons &amp; Sub-Mods</option>
                        <option value="patch">Downloads &amp; Patches</option>
                    `;
        } else {
            cFilter.innerHTML = `
                        <option value="all">All Categories</option>
                        <option value="patch">Engine Patches</option>
                        <option value="mod">Mods</option>
                        <option value="addon">Addons &amp; HUD</option>
                        <option value="map">Maps</option>
                    `;
        }
    }

    /**
     * Returns from the content detail view to the catalog browser.
     */
    function showDownloadsBrowserView() {
        const browserView = document.getElementById('ghDownloadsBrowserView');
        const detailView = document.getElementById('ghContentDetailView');
        if (browserView) browserView.style.display = '';
        if (detailView) detailView.style.display = 'none';
    }

    /**
     * Wires the publisher buttons, catalog search box and category filter.
     */
    function initDownloadsCatalog() {
        const pubBtns = document.querySelectorAll('.gh-pub-btn');
        pubBtns.forEach(btn => {
            btn.addEventListener('click', () => selectPublisher(btn, pubBtns));
        });

        const catalogSearch = document.getElementById('ghCatalogSearch');
        const catalogFilter = document.getElementById('ghCatalogFilter');
        if (catalogSearch) catalogSearch.addEventListener('input', filterDownloadsCatalog);
        if (catalogFilter) catalogFilter.addEventListener('change', filterDownloadsCatalog);
    }

    initDownloadsCatalog();

    // Initial filter run
    filterDownloadsCatalog();

    /**
     * Switches the downloads pane to the detail view for a catalog item.
     * @param {string} contentName Fallback item name.
     * @param {HTMLElement|null} card Catalog card that was opened.
     */
    function showContentDetail(contentName, card) {
        const browserView = document.getElementById('ghDownloadsBrowserView');
        const detailView = document.getElementById('ghContentDetailView');
        if (!detailView || !browserView) return;

        browserView.style.display = 'none';
        detailView.style.display = 'flex';

        if (card) {
            applyContentDetail(readContentCard(card, contentName));
        }
    }

    /**
     * Reads the display fields of a catalog card.
     * @param {HTMLElement} card Catalog card element.
     * @param {string} contentName Fallback item name.
     * @returns {{title: string, desc: string, image: string, badge: string}} Detail view fields.
     */
    function readContentCard(card, contentName) {
        return {
            title: readCardText(card, '.gh-content-title', contentName),
            desc: readCardText(card, '.gh-content-desc', ''),
            image: card.querySelector('.gh-banner-bg')?.getAttribute('src') || './assets/images/zerohour-cover.png',
            badge: readCardText(card, '.badge-tag', 'Package')
        };
    }

    /**
     * Reads the text of a card child element.
     * @param {HTMLElement} card Card element.
     * @param {string} selector Child selector.
     * @param {string} fallback Value used when the child is missing or empty.
     * @returns {string} The child text, or the fallback.
     */
    function readCardText(card, selector, fallback) {
        return card.querySelector(selector)?.textContent || fallback;
    }

    /**
     * Fills the content detail view with a catalog card's fields.
     * @param {{title: string, desc: string, image: string, badge: string}} details Detail view fields.
     */
    function applyContentDetail(details) {
        const iconEl = document.getElementById('ghDetailIconImg');

        setTextById('ghDetailTitle', details.title);
        setTextById('ghDetailBadge', details.badge);
        setTextById('ghDetailSummaryText', details.desc);
        if (iconEl) iconEl.src = details.image;
        setTextById('ghDetailAuthor', describeActivePublisherAuthor());
    }

    /**
     * Returns the author label for the active publisher.
     * @returns {string} Publisher title, or Community when unknown.
     */
    function describeActivePublisherAuthor() {
        return pubMeta[activePublisherKey]?.title || 'Community';
    }

    /**
     * Sets the text of the element with the given id, if it exists.
     * @param {string} id Element id.
     * @param {string} text New text content.
     */
    function setTextById(id, text) {
        const el = document.getElementById(id);
        if (el) el.textContent = text;
    }

    /**
     * Shows or hides a content detail subtab pane.
     * @param {string} id Pane element id.
     * @param {boolean} isVisible Whether the pane is shown.
     */
    function setDetailPaneDisplay(id, isVisible) {
        const pane = document.getElementById(id);
        if (pane) pane.style.display = isVisible ? 'block' : 'none';
    }

    /**
     * Wires the catalog cards, detail view navigation, subtabs and install button.
     */
    function initContentDetailView() {
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

                setDetailPaneDisplay('ghDetailPaneSummary', tabKey === 'summary');
                setDetailPaneDisplay('ghDetailPaneReleases', tabKey === 'releases');
                setDetailPaneDisplay('ghDetailPaneDependencies', tabKey === 'dependencies');
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
    }

    /**
     * Wires the add to profile modal and the manifests directory button.
     */
    function initAddToProfile() {
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
                fillProfileEditorHeader('New Profile', 'Custom Profile');
                openModal('ghGameProfileSettingsWindow');
            });
        }

        const openManifestBtn = document.getElementById('ghOpenManifestBtn');
        if (openManifestBtn) {
            openManifestBtn.addEventListener('click', () => {
                window.showGenHubToast('Info', 'Manifests Directory', 'Storage path: ~/.local/share/GenHub/manifests/');
            });
        }
    }

    initContentDetailView();
    initAddToProfile();

    // -------------------------------------------------------------
    // TOOLS TAB: REPLAY & MAP MANAGERS
    // -------------------------------------------------------------
    /**
     * Wires the Tools sidebar so each tool button shows its pane.
     */
    function initToolSelection() {
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
    }

    initToolSelection();

    /**
     * Shows only the map rows that match the selected game and search text.
     */
    function filterMaps() {
        const selectedGame = readSelectedMapGame();
        const mapSearchQuery = (document.getElementById('ghMapSearchInput')?.value || '').toLowerCase().trim();
        let count = 0;

        document.querySelectorAll('#ghMapTable tbody tr').forEach(row => {
            const show = mapRowMatches(readMapRow(row), selectedGame, mapSearchQuery);
            row.style.display = show ? '' : 'none';
            if (show) count++;
        });

        updateMapStatusBar(count, selectedGame);
    }

    /**
     * Returns the game selected by the map manager subtabs.
     * @returns {string} The data-map-game value of the active tab, or zerohour.
     */
    function readSelectedMapGame() {
        const activeTab = document.querySelector('[data-map-game].active');
        return activeTab ? activeTab.getAttribute('data-map-game') : 'zerohour';
    }

    /**
     * Reads the filterable attributes of a map table row.
     * @param {HTMLTableRowElement} row Map row.
     * @returns {{game: string, name: string}} Row game and lowercase map name.
     */
    function readMapRow(row) {
        return {
            game: row.getAttribute('data-game') || 'zerohour',
            name: (row.querySelector('.map-name')?.textContent || '').toLowerCase()
        };
    }

    /**
     * Tests a map row against the selected game and search query.
     * @param {{game: string, name: string}} rowInfo Row attributes.
     * @param {string} selectedGame Selected game key.
     * @param {string} query Lowercase search query.
     * @returns {boolean} True when the row should be shown.
     */
    function mapRowMatches(rowInfo, selectedGame, query) {
        const matchesGame = !selectedGame || rowInfo.game === selectedGame;
        return matchesGame && (!query || rowInfo.name.includes(query));
    }

    /**
     * Shows the visible map count and game in the map status bar.
     * @param {number} count Number of visible maps.
     * @param {string} selectedGame Selected game key.
     */
    function updateMapStatusBar(count, selectedGame) {
        const statusBar = document.getElementById('ghMapStatusBar');
        if (statusBar) {
            const gameLabel = selectedGame === 'generals' ? 'Generals' : 'Zero Hour';
            statusBar.textContent = `${count} Maps indexed (${gameLabel}) • 0 Selected`;
        }
    }

    /**
     * Wires the map manager game subtabs and search box.
     */
    function initMapFilters() {
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
    }

    /**
     * Wires the map import, browse and refresh buttons.
     */
    function initMapImport() {
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
    }

    /**
     * Wires the Tools sidebar actions and the map folder and map pack buttons.
     */
    function initToolSidebarActions() {
        const addToolBtn = document.getElementById('ghAddToolBtn');
        if (addToolBtn) {
            addToolBtn.addEventListener('click', () => {
                window.showGenHubToast('Tools', 'Tool Catalog', 'Scanning extension manifests... All 4 official tools are installed and ready.');
            });
        }

        const refreshToolsBtn = document.getElementById('ghRefreshToolsBtn');
        if (refreshToolsBtn) {
            refreshToolsBtn.addEventListener('click', () => {
                window.showGenHubToast('Tools', 'Tools Synchronized', 'Validated plugins: Replay Manager, Map Manager, Hotkeys Editor, and GenPatcher.');
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
    }

    /**
     * Wires the per-map integrity test and delete buttons.
     */
    function initMapRowActions() {
        document.querySelectorAll('[data-map-test]').forEach(btn => {
            btn.addEventListener('click', () => {
                const mapName = btn.getAttribute('data-map-test');
                window.showGenHubToast('Success', 'Integrity Verified', `Map "${mapName}" SHA-256 matches competitive standard.`);
            });
        });

        document.querySelectorAll('[data-map-del]').forEach(btn => {
            btn.addEventListener('click', () => {
                const mapName = btn.getAttribute('data-map-del');
                const row = btn.closest('tr');
                if (row) row.remove();
                window.showGenHubToast('Warning', 'Map Deleted', `Removed map "${mapName}" from local directory.`);
            });
        });
    }

    initMapFilters();

    // Initialize Map filtering on load
    filterMaps();

    initMapImport();
    initToolSidebarActions();
    initMapRowActions();

    // Replay Manager Actions (PR #422 CRC Catalog & Compatibility Integration)
    /**
     * Wires the replay search box.
     */
    function initReplaySearch() {
        const replaySearch = document.getElementById("ghReplaySearch");
        if (replaySearch) {
            replaySearch.addEventListener("input", (e) => {
                const searchQuery = e.target.value.toLowerCase().trim();
                document.querySelectorAll("#ghReplaysTable tbody tr").forEach(row => {
                    const text = row.textContent.toLowerCase();
                    row.style.display = text.includes(searchQuery) ? "" : "none";
                });
            });
        }
    }

    /**
     * Refreshes the selected replay counter.
     */
    function updateReplaySelectedCount() {
        const countSpan = document.getElementById("ghReplaySelectedCount");
        if (!countSpan) return;
        const selected = document.querySelectorAll("#ghReplaysTable tbody tr.selected").length;
        const total = document.querySelectorAll("#ghReplaysTable tbody tr").length;
        countSpan.innerHTML = `<strong>${selected || (total > 0 ? 1 : 0)}</strong> Selected`;
    }

    /**
     * Wires replay row selection and the delegated replay row actions.
     */
    function initReplayRows() {
        document.querySelectorAll("#ghReplaysTable tbody tr").forEach(row => {
            row.addEventListener("click", (e) => {
                if (e.target.closest("button") || e.target.closest("input")) return;
                row.classList.toggle("selected");
                updateReplaySelectedCount();
            });
        });

        document.getElementById("ghReplaysTable")?.addEventListener("click", handleReplayTableClick);
    }

    /**
     * Runs the replay row action of the clicked action button.
     * @param {MouseEvent} e Click event from the replay table.
     */
    function handleReplayTableClick(e) {
        const actBtn = e.target.closest("[data-rep-act]");
        if (!actBtn) return;
        e.stopPropagation();

        const act = actBtn.getAttribute("data-rep-act");
        const row = actBtn.closest("tr");
        const replay = readReplayRow(row);
        const action = replayRowActions.get(act);
        if (action) action(row, replay);
    }

    /**
     * Reads the match name, client and executable CRC of a replay row.
     * @param {HTMLTableRowElement|null} row Replay row.
     * @returns {{matchName: string, client: string, exeCrc: string}} Replay fields with defaults.
     */
    function readReplayRow(row) {
        return {
            matchName: readReplayMatchName(row),
            client: row?.getAttribute("data-rep-client") || "Zero Hour Client",
            exeCrc: row?.getAttribute("data-rep-exe") || "0x8F3A2B1C"
        };
    }

    /**
     * Reads the match name of a replay row.
     * @param {HTMLTableRowElement|null} row Replay row.
     * @returns {string} The match name, or Replay.
     */
    function readReplayMatchName(row) {
        return row ? (row.querySelector("strong")?.textContent || "Replay") : "Replay";
    }

    /**
     * Announces playback of a replay with its matched client.
     * @param {HTMLTableRowElement|null} row Replay row.
     * @param {{matchName: string, client: string, exeCrc: string}} replay Replay fields.
     */
    function launchReplay(row, replay) {
        window.showGenHubToast("Success", "Launching Replay", `Matched ${replay.client} (${replay.exeCrc}). Starting desync-free playback for "${replay.matchName}".`);
    }

    /**
     * Announces automatic profile creation for a replay's client.
     * @param {HTMLTableRowElement|null} row Replay row.
     * @param {{matchName: string, client: string, exeCrc: string}} replay Replay fields.
     */
    function setupReplayProfile(row, replay) {
        window.showGenHubToast("Info", "Auto-Configuring Profile", `Creating dedicated profile for ${replay.client} via CRC catalog mapping.`);
    }

    /**
     * Announces the client selector for a replay.
     * @param {HTMLTableRowElement|null} row Replay row.
     * @param {{matchName: string, client: string, exeCrc: string}} replay Replay fields.
     */
    function openReplayProfileOptions(row, replay) {
        window.showGenHubToast("Info", "Replay Profile Options", `Opening client selector dialog for "${replay.matchName}".`);
    }

    /**
     * Announces that the replay file was revealed in the file explorer.
     */
    function revealReplayFile() {
        window.showGenHubToast("Info", "Opening Explorer", "Revealed replay file in ~/.local/share/GenHub/Replays/");
    }

    /**
     * Removes a replay row and refreshes the selection counter.
     * @param {HTMLTableRowElement} row Replay row.
     * @param {{matchName: string, client: string, exeCrc: string}} replay Replay fields.
     */
    function deleteReplayRow(row, replay) {
        row.remove();
        updateReplaySelectedCount();
        window.showGenHubToast("Warning", "Replay Deleted", `Removed "${replay.matchName}" from local storage.`);
    }

    const replayRowActions = new Map([
        ["launch", launchReplay],
        ["setup", setupReplayProfile],
        ["profile", openReplayProfileOptions],
        ["folder", revealReplayFile],
        ["delete", deleteReplayRow]
    ]);

    /**
     * Wires the replay action bar delete button.
     */
    function initReplayDeleteSelected() {
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
    }

    /**
     * Wires the replay archive, upload and history buttons.
     */
    function initReplayArchiveActions() {
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
    }

    /**
     * Wires the replay import, refresh and folder buttons.
     */
    function initReplayImport() {
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
    }

    initReplaySearch();
    initReplayRows();
    initReplayDeleteSelected();
    initReplayArchiveActions();
    initReplayImport();

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

    /**
     * Shows only the hotkey cards of the active faction and category.
     */
    function filterHotkeyCards() {
        hotkeyCards.forEach(card => {
            const cFaction = card.getAttribute("data-faction");
            const cCat = card.getAttribute("data-cat");
            const matchFaction = cFaction === activeFaction;
            const matchCat = activeCat === "all" || cCat === activeCat;
            card.style.display = (matchFaction && matchCat) ? "flex" : "none";
        });
    }

    /**
     * Wires the hotkey faction tabs, category pills and general and profile selectors.
     */
    function initHotkeyFilters() {
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
                window.showGenHubToast("GenHotkeys", "General Profile", `Switched variant: ${label}`);
            });
        }

        if (profileSelect) {
            profileSelect.addEventListener("change", () => {
                const label = profileSelect.options[profileSelect.selectedIndex]?.text || "Profile";
                window.showGenHubToast("GenHotkeys", "Profile Switched", `Active configuration: ${label}`);
            });
        }
    }

    initHotkeyFilters();

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

    /**
     * Applies the selected hotkey preset to every matching command card.
     */
    function applyHotkeyPreset() {
        const presetKey = presetSelect.value;
        const presetMap = hotkeyPresets[presetKey];
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
            const label = presetSelect.options[presetSelect.selectedIndex]?.text || presetKey;
            window.showGenHubToast("GenHotkeys", "Preset Applied", `Loaded ${label} key mapping preset.`);
        }
    }

    /**
     * Moves every hotkey stamp to the selected corner of its cameo.
     */
    function applyHotkeyStampCorner() {
        const corner = cornerSelect.value;
        const placement = getStampPlacement(corner);
        const stamps = document.querySelectorAll(".gh-hotkey-stamp");
        stamps.forEach(s => positionHotkeyStamp(s, placement));
        const label = cornerSelect.options[cornerSelect.selectedIndex]?.text || corner;
        window.showGenHubToast("GenHotkeys", "Stamp Position", `Badges aligned to ${label}.`);
    }

    /**
     * Resolves which cameo edges a corner code touches.
     * @param {string} corner Corner code: tl, tr, bl or br.
     * @returns {{top: boolean, bottom: boolean, left: boolean, right: boolean}} Edges to anchor.
     */
    function getStampPlacement(corner) {
        return {
            top: corner === "tr" || corner === "tl",
            bottom: corner === "br" || corner === "bl",
            left: corner === "tl" || corner === "bl",
            right: corner === "tr" || corner === "br"
        };
    }

    /**
     * Anchors a hotkey stamp to the given cameo edges.
     * @param {HTMLElement} stamp Hotkey stamp element.
     * @param {{top: boolean, bottom: boolean, left: boolean, right: boolean}} placement Edges to anchor.
     */
    function positionHotkeyStamp(stamp, placement) {
        stamp.style.top = placement.top ? "3px" : "auto";
        stamp.style.bottom = placement.bottom ? "3px" : "auto";
        stamp.style.left = placement.left ? "4px" : "auto";
        stamp.style.right = placement.right ? "4px" : "auto";
    }

    /**
     * Wires the hotkey preset, stamp corner and overlay controls.
     */
    function initHotkeyDisplayOptions() {
        if (presetSelect) {
            presetSelect.addEventListener("change", applyHotkeyPreset);
        }

        if (cornerSelect) {
            cornerSelect.addEventListener("change", applyHotkeyStampCorner);
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
    }

    /**
     * Wires hotkey command card selection and the addon compile button.
     */
    function initHotkeyCards() {
        hotkeyCards.forEach(card => {
            card.addEventListener("click", () => {
                hotkeyCards.forEach(c => c.classList.remove("selected"));
                card.classList.add("selected");
                const title = card.querySelector(".gh-action-title")?.textContent || "Action";
                const stamp = card.querySelector(".gh-hotkey-stamp")?.textContent || "";
                window.showGenHubToast("Hotkeys Editor", "Command Selected", `${title} [${stamp}] ready for binding.`);
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
    }

    /**
     * Wires the GenPatcher apply fixes button.
     */
    function initGenPatcher() {
        const gpBtn = document.getElementById('ghApplyGenPatcherBtn');
        if (gpBtn) {
            gpBtn.addEventListener('click', () => {
                const orig = gpBtn.textContent;
                gpBtn.textContent = 'Applying...';
                setTimeout(() => {
                    gpBtn.textContent = '✓ Fixes Applied';
                    setTimeout(() => { gpBtn.textContent = orig; }, 1800);
                    window.showGenHubToast('Success', 'GenPatcher Applied', 'DirectDraw fix, camera zoom, and GenTool 8.9 installed.');
                }, 600);
            });
        }
    }

    initHotkeyDisplayOptions();
    initHotkeyCards();
    initGenPatcher();

    // -------------------------------------------------------------
    // SETTINGS TAB INTERACTIVITY (Avalonia Expander & Sidebar Nav)
    // -------------------------------------------------------------
    const settingNavBtns = document.querySelectorAll('.gh-setting-nav-btn');
    const settingsContent = document.getElementById('ghSettingsScrollContainer') || document.querySelector('.gh-settings-content');

    /**
     * Scrolls the settings pane to a section and highlights its sidebar button.
     * @param {HTMLElement|null} targetEl Settings expander to show.
     * @param {boolean} openIfClosed Whether to expand the section first.
     */
    function scrollToSettingsSection(targetEl, openIfClosed) {
        if (!targetEl || !settingsContent) return;
        if (openIfClosed) {
            expandSettingsSection(targetEl);
        }
        const targetTop = targetEl.offsetTop - settingsContent.offsetTop;
        settingsContent.scrollTo({
            top: Math.max(0, targetTop - 12),
            behavior: 'smooth'
        });
        const targetId = targetEl.id;
        if (targetId) {
            highlightSettingsNav(targetId);
        }
    }

    /**
     * Expands a collapsed settings section.
     * @param {HTMLElement} targetEl Settings expander.
     */
    function expandSettingsSection(targetEl) {
        if (targetEl.classList.contains('open')) return;
        targetEl.classList.add('open');
        const header = targetEl.querySelector('.gh-expander-header');
        if (header) header.setAttribute('aria-expanded', 'true');
    }

    /**
     * Marks the settings sidebar button for a section as active.
     * @param {string} targetId Section element id.
     */
    function highlightSettingsNav(targetId) {
        settingNavBtns.forEach(b => {
            if (b.getAttribute('data-s-target') === targetId) {
                b.classList.add('active');
            } else {
                b.classList.remove('active');
            }
        });
    }

    /**
     * Wires the settings expander headers and the expand and collapse all buttons.
     */
    function initSettingsExpanders() {
        document.querySelectorAll('.gh-expander-header').forEach(header => {
            header.addEventListener('click', (e) => {
                e.preventDefault();
                const expander = header.closest('.gh-settings-expander');
                if (expander) {
                    const willOpen = !expander.classList.contains('open');
                    expander.classList.toggle('open', willOpen);
                    header.setAttribute('aria-expanded', willOpen ? 'true' : 'false');
                    if (willOpen) {
                        scrollToSettingsSection(expander, false);
                    }
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
    }

    /**
     * Wires the manual game installation button (PR #452).
     */
    function initCustomInstallButton() {
        const addCustomInstallBtn = document.getElementById('ghAddCustomInstallBtn');
        if (addCustomInstallBtn) {
            addCustomInstallBtn.addEventListener('click', (e) => {
                e.preventDefault();
                registerCustomInstall(addCustomInstallBtn);
            });
        }
    }

    /**
     * Adds the simulated custom installation row once.
     * @param {HTMLElement} addCustomInstallBtn The add installation button.
     */
    function registerCustomInstall(addCustomInstallBtn) {
        const existing = document.getElementById('ghCustomInstallCustomRow');
        if (existing) {
            window.showGenHubToast('Settings', 'Installation Exists', 'Custom installation path is already registered and linked.');
            return;
        }
        const container = addCustomInstallBtn.closest('.gh-form-group');
        if (container) {
            const newRow = document.createElement('div');
            newRow.className = 'gh-install-row';
            newRow.id = 'ghCustomInstallCustomRow';
            newRow.innerHTML = `
                    <div>
                        <strong>D:\\Games\\Command &amp; Conquer Generals - Custom</strong>
                        <div style="font-size: 11px; color: #64748b;">Generals v1.08 • Custom Manual Installation</div>
                    </div>
                    <span class="gh-chip" style="color: #34d399;">Linked</span>
                `;
            container.appendChild(newRow);
            window.showGenHubToast('Success', 'Game Installation Registered', 'Discovered generals.exe v1.08 in D:\\Games\\Command & Conquer Generals - Custom');
        }
    }

    /**
     * Wires the settings sidebar so each button jumps to and opens its section.
     */
    function initSettingsNav() {
        settingNavBtns.forEach(btn => {
            btn.addEventListener('click', (e) => {
                e.preventDefault();
                e.stopPropagation();
                const targetId = btn.getAttribute('data-s-target');
                const targetEl = document.getElementById(targetId);
                if (targetEl) {
                    scrollToSettingsSection(targetEl, true);
                }
            });
        });
    }

    initSettingsExpanders();
    initCustomInstallButton();
    initSettingsNav();

    /**
     * Applies an accent color to the client theme variables and swatches.
     * @param {string} colorHex Accent color as #rrggbb.
     */
    function applyThemeAccent(colorHex) {
        if (!colorHex) return;
        const red = parseInt(colorHex.slice(1,3), 16) || 139;
        const green = parseInt(colorHex.slice(3,5), 16) || 92;
        const blue = parseInt(colorHex.slice(5,7), 16) || 246;

        const rDark = Math.max(0, Math.round(red * 0.82));
        const gDark = Math.max(0, Math.round(green * 0.82));
        const bDark = Math.max(0, Math.round(blue * 0.82));
        const colorHover = `rgb(${rDark}, ${gDark}, ${bDark})`;

        const glow = `rgba(${red}, ${green}, ${blue}, 0.45)`;
        const light = `rgba(${red}, ${green}, ${blue}, 0.18)`;
        const border = `rgba(${red}, ${green}, ${blue}, 0.5)`;

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

    /**
     * Wires the theme swatches, directory shortcuts, log buttons and memory cleanup button.
     */
    function initSettingsActions() {
        document.querySelectorAll('.gh-theme-swatch').forEach(sw => {
            sw.addEventListener('click', () => {
                const color = sw.getAttribute('data-color');
                applyThemeAccent(color);
                window.showGenHubToast('Success', 'Theme Swatch Applied', `Updated client accent color to ${color}`);
            });
        });

        document.querySelectorAll('.gh-dir-act-btn').forEach(btn => {
            btn.addEventListener('click', () => {
                const targetDirName = btn.getAttribute('data-dir');
                window.showGenHubToast('Info', 'Opened Directory', `Accessed path: ~/.local/share/GenHub/${targetDirName.replace(' ', '')}/`);
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
    }

    initSettingsActions();

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
                "title": "Profile Sharing & Inspection",
                "content": "Share configurations with friends via genhub:// links or standalone packages.",
                "type": "Feature",
                "detailed": "**Profile Sharing (PR #400):**\n             GenHub lets you distribute your exact mod setup, custom maps, and game configurations directly to friends with a single click.\n\n             **Sharing Features:**\n             -   **Protocol Links (genhub://):** Generates lightweight URI links you can paste in Discord or chat. Friends clicking the link open GenHub automatically.\n             -   **Standalone Archives (.ghprofile):** Export an offline package containing all profile configurations and dependency manifests.\n             -   **Rich Inspection Window:** Inspect required downloads, cached manifests, and game version compatibility before importing into your library."
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
        "title": "GenHub Changelog",
        "desc": "Official releases and version history from GitHub.",
        "cards": []
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
        "desc": "Official patch notes and service updates from playgenerals.online.",
        "cards": []
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

    const PROFILES_DEMO_HTML = `
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

    const SHORTCUTS_DEMO_HTML = `
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

    const STEAM_DEMO_HTML = `
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

    const SCAN_GAMES_DEMO_HTML = `
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

    const WORKSPACES_DEMO_HTML = `
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

    const demoTemplates = new Map([
        ['profiles', PROFILES_DEMO_HTML],
        ['shortcuts', SHORTCUTS_DEMO_HTML],
        ['steam', STEAM_DEMO_HTML],
        ['scangames', SCAN_GAMES_DEMO_HTML],
        ['workspace', WORKSPACES_DEMO_HTML],
        ['workspaces', WORKSPACES_DEMO_HTML]
    ]);

    /**
     * Returns the interactive demo markup shown above an Info section.
     * @param {string} secId Info section id.
     * @returns {string} Demo HTML, or an empty string when the section has none.
     */
    function renderDemoContainer(secId) {
        return demoTemplates.get(secId) || '';
    }

    /**
     * Converts the small Markdown subset used by Info cards into HTML.
     * @param {string} markdownText Markdown source.
     * @returns {string} HTML markup.
     */
    function formatMarkdown(markdownText) {
        if (!markdownText) return "";
        let html = markdownText
            .replace(/\[(.*?)\]\((https?:\/\/[^\s)]+)\)/g, "<a href=\"$2\" target=\"_blank\" rel=\"noopener noreferrer\" style=\"color: #a78bfa; text-decoration: underline;\">$1</a>")
            .replace(/\*\*(.*?)\*\*/g, "<strong>$1</strong>")
            .replace(/\*(\S[^*]*?)\*/g, "<em>$1</em>")
            .replace(/`([^`]+)`/g, "<code style=\"background: rgba(255,255,255,0.08); padding: 1px 4px; border-radius: 3px; font-family: monospace; font-size: 12px; color: #cbd5e1;\">$1</code>")
            .replace(/^###\s+(.*)$/gm, "<h5 style=\"color: #f1f5f9; margin: 10px 0 4px 0; font-size: 13px; font-weight: 600;\">$1</h5>")
            .replace(/^##\s+(.*)$/gm, "<h4 style=\"color: #f1f5f9; margin: 12px 0 6px 0; font-size: 14px; font-weight: 700;\">$1</h4>")
            .replace(/^\s*[*-]\s+(.*)$/gm, "<li>$1</li>");
        html = html.replace(/((?:<li>.*?<\/li>\s*)+)/g, "<ul style=\"margin: 8px 0; padding-left: 20px;\">$1</ul>");
        html = html.replace(/<br>\s*<(ul|li|\/ul|\/li|h4|h5)/g, "<$1").replace(/<\/(ul|li|h4|h5)>\s*<br>/g, "</$1>");
        return html.replace(/\n/g, "<br>");
    }

    /**
     * Returns the first two plain prose lines of a release body.
     * @param {string} rawBody Release notes Markdown.
     * @returns {string[]} Up to two summary lines.
     */
    function extractSummaryLines(rawBody) {
        return (rawBody || "").split("\n")
            .map(lineText => lineText.trim())
            .filter(lineText => Boolean(lineText) && !/^([#*!-]|\s*$)/.test(lineText))
            .slice(0, 2);
    }

    /**
     * Builds the one-line summary of a GenHub release.
     * @param {string} rawBody Release notes Markdown.
     * @returns {string} Summary text.
     */
    function formatChangelogSummary(rawBody) {
        const summaryLines = extractSummaryLines(rawBody);
        if (summaryLines.length === 0) {
            return "Official GenHub release with updated assets and dependencies.";
        }
        return summaryLines.join(" ");
    }

    /**
     * Formats a release publish date for display.
     * @param {string} publishedAt ISO publish timestamp.
     * @returns {string} Short US date, or an empty string.
     */
    function formatReleaseDate(publishedAt) {
        if (!publishedAt) {
            return "";
        }
        return new Date(publishedAt).toLocaleDateString("en-US", { year: "numeric", month: "short", day: "numeric" });
    }

    /**
     * Returns the card title for a GitHub release.
     * @param {object} release GitHub release.
     * @returns {string} Release name, or GenHub followed by the tag.
     */
    function getChangelogCardTitle(release) {
        const tagName = release.tag_name || "";
        return release.name || `GenHub ${tagName}`;
    }

    /**
     * Returns the Markdown header block with the release date.
     * @param {string} dateStr Formatted release date.
     * @returns {string} Header Markdown.
     */
    function getReleaseHeaderBlock(dateStr) {
        if (dateStr) {
            return `**Published:** ${dateStr}\n\n`;
        }
        return "\n";
    }

    /**
     * Returns the card summary text for a release.
     * @param {string} dateStr Formatted release date.
     * @param {string} summaryText Release summary.
     * @returns {string} Card content text.
     */
    function getReleaseCardContent(dateStr, summaryText) {
        if (dateStr) {
            return `Released on ${dateStr}. ${summaryText}`;
        }
        return summaryText;
    }

    /**
     * Returns the chip label for a release.
     * @param {boolean} isPrerelease Whether the release is a prerelease.
     * @returns {string} Pre-release or Release.
     */
    function getReleaseType(isPrerelease) {
        if (isPrerelease) {
            return "Pre-release";
        }
        return "Release";
    }

    /**
     * Builds an Info card from a GitHub release.
     * @param {object} release GitHub release.
     * @returns {{title: string, type: string, content: string, detailed: string}} Info card data.
     */
    function createChangelogCard(release) {
        const tagName = release.tag_name || "";
        const cardTitle = getChangelogCardTitle(release);
        const dateStr = formatReleaseDate(release.published_at);
        const rawBody = release.body || "No release notes provided.";
        const summaryText = formatChangelogSummary(rawBody);
        const cardContent = getReleaseCardContent(dateStr, summaryText);
        const releaseUrl = release.html_url || "https://github.com/community-outpost/GenHub/releases";
        const dateBlock = getReleaseHeaderBlock(dateStr);
        const releaseType = getReleaseType(release.prerelease);

        return {
            title: cardTitle,
            type: releaseType,
            content: cardContent,
            detailed: `**Release:** [${tagName}](${releaseUrl})\n${dateBlock}${rawBody}`
        };
    }

    /**
     * Builds the Markdown change list of a Generals Online patch note.
     * @param {object} note Patch note entry.
     * @returns {string} Markdown bullet list.
     */
    function getPatchNotesDetailsList(note) {
        if (Array.isArray(note.details) && note.details.length > 0) {
            return note.details.map(detailItem => `* ${detailItem}`).join("\n");
        }
        if (note.summary) {
            return `* ${note.summary}`;
        }
        return "* Stability and performance improvements.";
    }

    /**
     * Builds an Info card from a Generals Online patch note.
     * @param {object} note Patch note entry.
     * @returns {{title: string, type: string, content: string, detailed: string}} Info card data.
     */
    function createPatchNotesCard(note) {
        const detailsList = getPatchNotesDetailsList(note);
        const detailedMarkdown = `**Official Patch Notes:** [${note.title} on playgenerals.online](${note.url})\n**Date:** ${note.date}\n\n**Changes & Fixes:**\n${detailsList}`;

        return {
            title: `${note.title} (${note.date})`,
            type: "Patch Notes",
            content: note.summary || "Generals Online service updates and gameplay improvements.",
            detailed: detailedMarkdown
        };
    }

    const changelogState = {
        "changelog": { status: "idle", promise: null },
        "gochange": { status: "idle", promise: null }
    };

    /**
     * Loads a changelog section once and re-renders it if it is still selected.
     * @param {string} secKey Changelog section key: changelog or gochange.
     * @returns {Promise<void>} Resolves when the section has loaded or failed.
     */
    async function lazyLoadChangelog(secKey) {
        if (changelogState[secKey].status === "loaded") {
            return;
        }
        if (changelogState[secKey].status === "loading") {
            await changelogState[secKey].promise;
            return;
        }

        changelogState[secKey].status = "loading";

        showChangelogLoadingCard(secKey);

        const promise = loadChangelogSection(secKey);

        changelogState[secKey].promise = promise;
        await promise;

        rerenderInfoSectionIfActive(secKey);
    }

    /**
     * Shows the loading card for a changelog section.
     * @param {string} secKey Changelog section key.
     */
    function showChangelogLoadingCard(secKey) {
        const container = document.getElementById("ghInfoCardsContainer");
        if (container) {
            const isGH = secKey === "changelog";
            const label = isGH ? "GenHub Releases" : "Generals Online Patch Notes";
            const sourceUrl = isGH ? "https://github.com/community-outpost/GenHub/releases" : "https://www.playgenerals.online/patchnotes";
            container.innerHTML = `
                <div class="gh-info-card" style="text-align: center; padding: 40px 20px; border-color: rgba(139, 92, 246, 0.3);">
                    <div class="gh-loading-spinner" style="display: inline-block; width: 30px; height: 30px; border: 3px solid rgba(139, 92, 246, 0.25); border-top-color: #8b5cf6; border-radius: 50%; margin-bottom: 14px;"></div>
                    <h4 style="margin: 0 0 6px 0; color: #f8fafc; font-size: 14px; font-weight: 600;">Fetching ${label}...</h4>
                    <p style="margin: 0; font-size: 12.5px; color: #94a3b8;">
                        Connecting to <a href="${sourceUrl}" target="_blank" rel="noopener noreferrer" style="color: #a78bfa; text-decoration: underline;">${sourceUrl}</a>
                    </p>
                </div>
            `;
        }
    }

    /**
     * Starts loading the cards of a changelog section.
     * @param {string} secKey Changelog section key.
     * @returns {Promise<void>} Resolves when loading has finished.
     */
    async function loadChangelogSection(secKey) {
        if (secKey === "changelog") {
            await loadGenHubReleaseCards();
        } else if (secKey === "gochange") {
            await loadPatchNotesCards();
        }
    }

    /**
     * Loads the GenHub release cards, falling back to an error card.
     * @returns {Promise<void>} Resolves when the cards are set.
     */
    async function loadGenHubReleaseCards() {
        try {
            const releases = await fetchGenHubReleases();

            if (Array.isArray(releases) && releases.length > 0) {
                infoData.changelog.cards = releases.map(createChangelogCard);
                changelogState.changelog.status = "loaded";
            } else {
                throw new Error("No release data found");
            }
        } catch (err) {
            console.error("Failed to load GenHub changelog:", err);
            changelogState.changelog.status = "error";
            infoData.changelog.cards = [{
                title: "Unable to Load Releases",
                type: "Notice",
                content: "Could not fetch releases from GitHub API at this moment.",
                detailed: "Please view releases directly at [GitHub Releases](https://github.com/community-outpost/GenHub/releases)."
            }];
        }
    }

    /**
     * Fetches GenHub releases from the GitHub API, then from the local dataset.
     * @returns {Promise<Array|null>} Release list, or null when neither source has data.
     */
    async function fetchGenHubReleases() {
        const releases = await fetchGitHubApiReleases();
        if (releases) {
            return releases;
        }
        return fetchLocalReleases();
    }

    /**
     * Fetches GenHub releases from the GitHub Releases API.
     * @returns {Promise<Array|null>} A non-empty release list, or null.
     */
    async function fetchGitHubApiReleases() {
        try {
            const res = await fetch("https://api.github.com/repos/community-outpost/GenHub/releases");
            if (res.ok) {
                const data = await res.json();
                if (Array.isArray(data) && data.length > 0) {
                    return data;
                }
            }
        } catch (netErr) {
            console.warn("Direct GitHub API fetch failed, trying local dataset:", netErr);
        }
        return null;
    }

    /**
     * Fetches the cached GenHub release dataset bundled with the landing page.
     * @returns {Promise<*>} Parsed dataset, or null when unavailable.
     */
    async function fetchLocalReleases() {
        const localRes = await fetch("./assets/data/genhub_releases.json");
        if (localRes.ok) {
            return localRes.json();
        }
        return null;
    }

    /**
     * Loads the Generals Online patch note cards, falling back to an error card.
     * @returns {Promise<void>} Resolves when the cards are set.
     */
    async function loadPatchNotesCards() {
        try {
            let notes = null;
            const localRes = await fetch("./assets/data/generals_online_patchnotes.json");
            if (localRes.ok) {
                notes = await localRes.json();
            }

            if (Array.isArray(notes) && notes.length > 0) {
                infoData.gochange.cards = notes.map(createPatchNotesCard);
                changelogState.gochange.status = "loaded";
            } else {
                throw new Error("No patch notes data found");
            }
        } catch (err) {
            console.error("Failed to load Generals Online patch notes:", err);
            changelogState.gochange.status = "error";
            infoData.gochange.cards = [{
                title: "Unable to Load Patch Notes",
                type: "Notice",
                content: "Could not load patch notes at this time.",
                detailed: "Please view patch notes directly at [playgenerals.online/patchnotes](https://www.playgenerals.online/patchnotes)."
            }];
        }
    }

    /**
     * Re-renders an Info section if its navigation button is still active.
     * @param {string} secKey Info section key.
     */
    function rerenderInfoSectionIfActive(secKey) {
        const currentActiveBtn = document.querySelector("#ghInfoNavList .gh-info-nav-btn.active");
        const currentActiveId = currentActiveBtn ? currentActiveBtn.getAttribute("data-info-id") : null;
        if (currentActiveId === secKey) {
            renderInfoSection(secKey);
        }
    }

    const GENHUB_CHANGELOG_BANNER = `
                    <div style="display: flex; justify-content: space-between; align-items: center; padding: 10px 14px; background: rgba(139, 92, 246, 0.08); border: 1px solid rgba(139, 92, 246, 0.2); border-radius: 8px; margin-bottom: 12px; font-size: 12px; color: #cbd5e1;">
                        <span>Source: <a href="https://github.com/community-outpost/GenHub/releases" target="_blank" rel="noopener noreferrer" style="color: #a78bfa; font-weight: 600; text-decoration: underline;">github.com/community-outpost/GenHub/releases</a></span>
                        <button class="gh-btn-table" id="ghReloadChangelogBtn" style="font-size: 11px; padding: 2px 8px; height: 24px; cursor: pointer;">\u21bb Refresh</button>
                    </div>`;
    const GO_CHANGELOG_BANNER = `
                    <div style="display: flex; justify-content: space-between; align-items: center; padding: 10px 14px; background: rgba(59, 130, 246, 0.08); border: 1px solid rgba(59, 130, 246, 0.2); border-radius: 8px; margin-bottom: 12px; font-size: 12px; color: #cbd5e1;">
                        <span>Source: <a href="https://www.playgenerals.online/patchnotes" target="_blank" rel="noopener noreferrer" style="color: #60a5fa; font-weight: 600; text-decoration: underline;">playgenerals.online/patchnotes</a></span>
                        <button class="gh-btn-table" id="ghReloadChangelogBtn" style="font-size: 11px; padding: 2px 8px; height: 24px; cursor: pointer;">\u21bb Refresh</button>
                    </div>`;
    const changelogBanners = new Map([
        ["changelog", GENHUB_CHANGELOG_BANNER],
        ["gochange", GO_CHANGELOG_BANNER]
    ]);

    /**
     * Renders an Info section, lazy-loading changelog sections on first use.
     * @param {string} requestedKey Info section key or alias.
     */
    function renderInfoSection(requestedKey) {
        const secKey = resolveInfoSectionKey(requestedKey);

        // Lazy-load changelogs on demand when clicked
        if (isUnloadedChangelog(secKey)) {
            showInfoSectionHeading(infoData[secKey]);
            lazyLoadChangelog(secKey);
            return;
        }

        const section = infoData[secKey] || infoData.quickstart;
        const container = document.getElementById("ghInfoCardsContainer");
        showInfoSectionHeading(section);
        resetInfoContentScroll();

        if (container) {
            renderInfoSectionCards(container, secKey, section);
        }
    }

    /**
     * Maps Info section aliases to their section keys.
     * @param {string} secKey Requested section key.
     * @returns {string} Resolved section key.
     */
    function resolveInfoSectionKey(secKey) {
        if (secKey === "workspace" && infoData.workspaces) return "workspaces";
        if (secKey === "faq" && infoData.gofaq) return "gofaq";
        return secKey;
    }

    /**
     * Tests whether a section is a changelog that has not loaded yet.
     * @param {string} secKey Info section key.
     * @returns {boolean} True for an unloaded changelog section.
     */
    function isUnloadedChangelog(secKey) {
        return (secKey === "changelog" || secKey === "gochange") && changelogState[secKey].status !== "loaded";
    }

    /**
     * Shows an Info section's title and description.
     * @param {{title: string, desc: string}|undefined} section Info section data.
     */
    function showInfoSectionHeading(section) {
        if (!section) return;
        setTextById("ghInfoSectionTitle", section.title);
        setTextById("ghInfoSectionDesc", section.desc);
    }

    /**
     * Renders an Info section's demo, source banner and cards, then wires them.
     * @param {HTMLElement} container Info cards container.
     * @param {string} secKey Info section key.
     * @param {{cards: Array}} section Info section data.
     */
    function renderInfoSectionCards(container, secKey, section) {
        const demoHtml = renderDemoContainer(secKey);
        const sourceBanner = changelogBanners.get(secKey) || "";
        const cardsHtml = (section.cards || []).map(renderInfoCard).join("");

        container.innerHTML = demoHtml + sourceBanner + cardsHtml;

        container.querySelectorAll(".gh-info-expand-btn").forEach(wireInfoCardExpander);
        wireChangelogReloadButton(container, secKey);

        // Wire Demo Profile Card launch / action buttons inside Demo container
        const demoCard = container.querySelector(".gh-profile-card");
        if (demoCard) {
            wireProfileCard(demoCard);
        }
    }

    /**
     * Renders one Info card.
     * @param {{title: string, type: string, content: string, detailed: string}} card Info card data.
     * @returns {string} Card HTML.
     */
    function renderInfoCard(card) {
        return `
                <div class="gh-info-card">
                    <div class="gh-info-card-header">
                        <h4>${card.title}</h4>
                        <span class="gh-chip">${card.type || "Concept"}</span>
                    </div>
                    <p style="font-size: 13.5px; color: #cbd5e1; line-height: 1.5; margin: 8px 0;">${card.content}</p>
                    ${card.detailed ? `<button class="gh-info-expand-btn">Show Details \u25be</button>
                    <div class="gh-info-detailed-content" style="font-size: 13px; color: #94a3b8; line-height: 1.6; padding-top: 10px;">
                        ${formatMarkdown(card.detailed)}
                    </div>` : ""}
                </div>
            `;
    }

    /**
     * Wires an Info card's show and hide details button.
     * @param {HTMLElement} expandBtn Expand button.
     */
    function wireInfoCardExpander(expandBtn) {
        expandBtn.addEventListener("click", () => {
            const content = expandBtn.nextElementSibling;
            if (content) {
                const isShown = content.classList.toggle("active");
                expandBtn.textContent = isShown ? "Hide Details \u25b4" : "Show Details \u25be";
            }
        });
    }

    /**
     * Wires the changelog refresh button if the section rendered one.
     * @param {HTMLElement} container Info cards container.
     * @param {string} secKey Info section key.
     */
    function wireChangelogReloadButton(container, secKey) {
        const reloadBtn = container.querySelector("#ghReloadChangelogBtn");
        if (reloadBtn) {
            reloadBtn.addEventListener("click", () => {
                changelogState[secKey].status = "idle";
                changelogState[secKey].promise = null;
                lazyLoadChangelog(secKey);
            });
        }
    }

    /**
     * Activates an Info navigation button and renders its section.
     * @param {HTMLElement} btn Clicked navigation button.
     * @param {NodeListOf<HTMLElement>} navButtons Buttons whose active state is cleared.
     */
    function selectInfoNavButton(btn, navButtons) {
        navButtons.forEach(b => b.classList.remove('active'));
        btn.classList.add('active');
        renderInfoSection(btn.getAttribute('data-info-id'));
        if (typeof btn.scrollIntoView === 'function') {
            btn.scrollIntoView({ behavior: 'smooth', block: 'nearest', inline: 'nearest' });
        }
    }

    const GUIDE_NAV_HTML = `
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
    const FAQ_NAV_HTML = `
                        <button class="gh-info-nav-btn active" data-info-id="zh_problems_game">Problems with the Game</button>
                        <button class="gh-info-nav-btn" data-info-id="zh_problems_multiplayer">Problems with Multiplayer</button>
                        <button class="gh-info-nav-btn" data-info-id="zh_general_faq">Frequently Asked Questions</button>
                    `;
    const CHANGELOGS_NAV_HTML = `
                        <button class="gh-info-nav-btn active" data-info-id="changelog">GenHub Changelog</button>
                        <button class="gh-info-nav-btn" data-info-id="gochange">Generals Online Changelog</button>
                    `;
    const infoModules = new Map([
        ['guide', { navHtml: GUIDE_NAV_HTML, firstSection: 'quickstart' }],
        ['faq', { navHtml: FAQ_NAV_HTML, firstSection: 'zh_problems_game' }],
        ['changelogs', { navHtml: CHANGELOGS_NAV_HTML, firstSection: 'changelog' }]
    ]);

    /**
     * Switches the Info navigation list to the selected module and renders its first section.
     * @param {Event} e Change event from the module selector.
     */
    function switchInfoModule(e) {
        const infoModule = infoModules.get(e.target.value);
        const navList = document.getElementById('ghInfoNavList');
        if (!infoModule) return;

        if (navList) {
            populateInfoNavList(navList, infoModule.navHtml);
        }
        renderInfoSection(infoModule.firstSection);
    }

    /**
     * Replaces the Info navigation buttons and wires them.
     * @param {HTMLElement} navList Info navigation list.
     * @param {string} navHtml Navigation button markup.
     */
    function populateInfoNavList(navList, navHtml) {
        navList.innerHTML = navHtml;
        navList.querySelectorAll('.gh-info-nav-btn').forEach(b => {
            b.addEventListener('click', () => {
                selectInfoNavButton(b, navList.querySelectorAll('.gh-info-nav-btn'));
            });
        });
    }

    /**
     * Toggles the Steam demo button between active and paused.
     * @param {HTMLElement} steamBtn Steam demo button.
     */
    function toggleDemoSteamButton(steamBtn) {
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

    /**
     * Wires the Info navigation, module selector and the delegated Steam demo button.
     */
    function initInfoTab() {
        infoNavBtns.forEach(btn => {
            btn.addEventListener('click', () => selectInfoNavButton(btn, infoNavBtns));
        });

        // Module Selector Dropdown (GenHub Guides, Zero Hour FAQ, Changelogs)
        const infoModuleSelect = document.getElementById('ghInfoModuleSelect');
        if (infoModuleSelect) {
            infoModuleSelect.addEventListener('change', switchInfoModule);
        }
    }

    initInfoTab();

    // Initial render of Quickstart Guide
    renderInfoSection('quickstart');

    // Delegated listener for dynamically rendered demo buttons & expanders
    document.addEventListener('click', (e) => {
        const steamBtn = e.target.closest('#ghDemoSteamToggleBtn');
        if (steamBtn) {
            toggleDemoSteamButton(steamBtn);
        }
    });

})();
