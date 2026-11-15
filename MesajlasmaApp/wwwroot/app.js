const PAGE_SIZE = 50;
const EMOJIS = [
    '😀', '😂', '🥹', '😍', '🤩', '😎',
    '🤔', '😴', '🥳', '😭', '😡', '🤯',
    '👍', '👎', '👏', '🙏', '🔥', '❤️',
    '💜', '✨', '🎉', '🚀', '💯', '✅',
];

const state = {
    isRegisterMode: false,
    me: null,
    conversations: [],
    searchUsers: [],
    searchQuery: '',
    activeConversationId: null,
    messages: [],
    hasOlderMessages: false,
    inChatSearchMode: false,
    pendingFile: null,
    editingMessage: null,
    typingUsers: new Map(), // conversationId -> timeoutId
    selectionVersion: 0,
};

let connection = null;
let typingSendTimeout = null;
let isSendingTyping = false;
let userSearchDebounce = null;

// ============================================================================
// Başlangıç ve Oturum
// ============================================================================

document.addEventListener('DOMContentLoaded', async () => {
    bindEvents();
    buildEmojiPicker();
    await restoreSession();
});

async function restoreSession() {
    try {
        state.me = await api('/api/auth/me');
        await enterApp();
    } catch {
        showAuthScreen();
    }
}

function showAuthScreen() {
    document.getElementById('appContainer').hidden = true;
    document.getElementById('profileModal').hidden = true;
    document.getElementById('authContainer').hidden = false;
}

async function enterApp() {
    document.getElementById('authContainer').hidden = true;
    document.getElementById('appContainer').hidden = false;
    renderCurrentUser();
    await startRealtime();
    await loadConversations();
}

function toggleAuthMode(event) {
    event.preventDefault();
    state.isRegisterMode = !state.isRegisterMode;
    document.getElementById('authError').hidden = true;

    const nameField = document.getElementById('registerNameField');
    const nameInput = document.getElementById('authName');
    const passwordInput = document.getElementById('authPassword');

    if (state.isRegisterMode) {
        document.getElementById('authTitle').textContent = 'Kayıt Ol';
        document.getElementById('authSubtitle').textContent = 'Yeni bir hesap oluşturun';
        nameField.hidden = false;
        nameInput.required = true;
        passwordInput.autocomplete = 'new-password';
        document.getElementById('authSubmitBtn').textContent = 'Kayıt Ol ve Bağlan';
        document.getElementById('authSwitchPrompt').textContent = 'Zaten hesabınız var mı?';
        document.getElementById('authSwitchLink').textContent = 'Giriş Yap';
    } else {
        document.getElementById('authTitle').textContent = 'Giriş Yap';
        document.getElementById('authSubtitle').textContent = 'Mesajlaşma ağına bağlanın';
        nameField.hidden = true;
        nameInput.required = false;
        passwordInput.autocomplete = 'current-password';
        document.getElementById('authSubmitBtn').textContent = 'Giriş Yap';
        document.getElementById('authSwitchPrompt').textContent = 'Hesabınız yok mu?';
        document.getElementById('authSwitchLink').textContent = 'Kayıt Ol';
    }
}

async function handleAuthSubmit(event) {
    event.preventDefault();
    const errorBox = document.getElementById('authError');
    const submitBtn = document.getElementById('authSubmitBtn');
    errorBox.hidden = true;

    const email = document.getElementById('authEmail').value.trim();
    const password = document.getElementById('authPassword').value;
    const displayName = document.getElementById('authName').value.trim();

    submitBtn.disabled = true;
    try {
        state.me = state.isRegisterMode
            ? await api('/api/auth/register', { method: 'POST', body: { displayName, email, password } })
            : await api('/api/auth/login', { method: 'POST', body: { email, password } });
        document.getElementById('authForm').reset();
        await enterApp();
    } catch (error) {
        errorBox.textContent = error.message;
        errorBox.hidden = false;
    } finally {
        submitBtn.disabled = false;
    }
}

async function logout() {
    try {
        await connection?.stop();
    } catch { /* yoksay */ }
    connection = null;

    await api('/api/auth/logout', { method: 'POST' }).catch(() => {});
    state.me = null;
    state.conversations = [];
    state.activeConversationId = null;
    state.messages = [];
    resetChatPane();
    showAuthScreen();
}

function renderCurrentUser() {
    if (!state.me) return;
    document.getElementById('currentUserName').textContent = state.me.displayName;
    document.getElementById('currentUserAvatar').src = avatarSrc(state.me);
    document.getElementById('modalAvatar').src = avatarSrc(state.me);

    const badge = document.getElementById('currentUserStatus');
    const text = document.getElementById('currentUserStatusText');
    badge.classList.toggle('invisible', state.me.isInvisible);
    text.textContent = state.me.isInvisible ? 'Görünmez' : 'Çevrimiçi';
}

// ============================================================================
// SignalR Gerçek Zamanlı Bağlantı
// ============================================================================

async function startRealtime() {
    if (!window.signalR) return;
    await connection?.stop().catch(() => {});

    const banner = document.getElementById('connectionBanner');
    connection = new signalR.HubConnectionBuilder()
        .withUrl('/hubs/chat')
        .withAutomaticReconnect([0, 1500, 3000, 5000, 10000])
        .build();

    connection.onreconnecting(() => { banner.hidden = false; });
    connection.onreconnected(async () => {
        banner.hidden = true;
        await loadConversations();
        if (state.activeConversationId && !state.inChatSearchMode) {
            await reloadActiveMessages();
        }
    });
    connection.onclose(() => { banner.hidden = false; });

    connection.on('MessageCreated', onMessageCreated);
    connection.on('MessageUpdated', onMessageUpdated);
    connection.on('MessageHidden', onMessageHidden);
    connection.on('ConversationDelivered', onConversationDelivered);
    connection.on('ConversationRead', onConversationRead);
    connection.on('PresenceChanged', onPresenceChanged);
    connection.on('UserUpdated', onUserUpdated);
    connection.on('ProfileUpdated', (me) => { state.me = me; renderCurrentUser(); });
    connection.on('Typing', onPartnerTyping);

    try {
        await connection.start();
        banner.hidden = true;
    } catch {
        banner.hidden = false;
    }
}

async function onMessageCreated(message) {
    let conversation = state.conversations.find(item => item.id === message.conversationId);
    if (!conversation) {
        await loadConversations();
        conversation = state.conversations.find(item => item.id === message.conversationId);
    }

    const isIncoming = message.senderId !== state.me.id;
    const isActive = message.conversationId === state.activeConversationId;

    if (conversation) {
        conversation.lastMessage = message;
        conversation.updatedAt = message.sentAt;
        if (isIncoming && !isActive) {
            conversation.unreadCount = (conversation.unreadCount || 0) + 1;
        }
        sortConversations();
        renderSidebarList();
    }

    if (isActive && !state.inChatSearchMode) {
        if (!state.messages.some(item => item.id === message.id)) {
            state.messages.push(message);
            renderMessages({ scrollToBottom: true });
        }
        if (isIncoming) {
            markConversationRead(message.conversationId);
        }
    }
}

function onMessageUpdated(message) {
    const conversation = state.conversations.find(item => item.id === message.conversationId);
    if (conversation?.lastMessage?.id === message.id) {
        conversation.lastMessage = message;
        renderSidebarList();
    }

    const index = state.messages.findIndex(item => item.id === message.id);
    if (index !== -1) {
        state.messages[index] = message;
        renderMessages({ preserveScroll: true });
    }
}

function onMessageHidden({ conversationId, messageId }) {
    if (conversationId === state.activeConversationId) {
        state.messages = state.messages.filter(item => item.id !== messageId);
        renderMessages({ preserveScroll: true });
    }
    loadConversations();
}

function onConversationDelivered({ conversationId, userId, deliveredAt }) {
    if (userId === state.me?.id) return;
    const threshold = new Date(deliveredAt).getTime();

    const conversation = state.conversations.find(item => item.id === conversationId);
    if (conversation?.lastMessage && conversation.lastMessage.senderId === state.me.id && conversation.lastMessage.status === 'sent') {
        conversation.lastMessage.status = 'delivered';
    }

    if (conversationId === state.activeConversationId) {
        for (const msg of state.messages) {
            if (msg.senderId === state.me.id && msg.status === 'sent' && new Date(msg.sentAt).getTime() <= threshold) {
                msg.status = 'delivered';
            }
        }
        renderMessages({ preserveScroll: true });
    }
}

function onConversationRead({ conversationId, userId, readAt }) {
    const conversation = state.conversations.find(item => item.id === conversationId);
    if (userId === state.me?.id) {
        if (conversation) {
            conversation.unreadCount = 0;
            renderSidebarList();
        }
        return;
    }

    const threshold = new Date(readAt).getTime();
    if (conversation?.lastMessage && conversation.lastMessage.senderId === state.me.id) {
        conversation.lastMessage.status = 'read';
    }

    if (conversationId === state.activeConversationId) {
        for (const msg of state.messages) {
            if (msg.senderId === state.me.id && new Date(msg.sentAt).getTime() <= threshold) {
                msg.status = 'read';
            }
        }
        renderMessages({ preserveScroll: true });
    }
}

function onPresenceChanged({ userId, isOnline, lastSeenAt }) {
    for (const conversation of state.conversations) {
        if (conversation.otherUser.id === userId) {
            conversation.otherUser.isOnline = isOnline;
            conversation.otherUser.lastSeenAt = lastSeenAt;
        }
    }
    renderSidebarList();
    updatePartnerHeader();
}

function onUserUpdated(user) {
    for (const conversation of state.conversations) {
        if (conversation.otherUser.id === user.id) {
            conversation.otherUser = user;
        }
    }
    renderSidebarList();
    updatePartnerHeader();
}

function onPartnerTyping({ conversationId, userId, isTyping }) {
    if (userId === state.me?.id) return;
    clearTimeout(state.typingUsers.get(conversationId));
    if (isTyping) {
        const timer = setTimeout(() => {
            state.typingUsers.delete(conversationId);
            updatePartnerHeader();
            renderSidebarList();
        }, 3000);
        state.typingUsers.set(conversationId, timer);
    } else {
        state.typingUsers.delete(conversationId);
    }
    updatePartnerHeader();
    renderSidebarList();
}

// ============================================================================
// Sohbet Listesi ve Kişi Arama
// ============================================================================

async function loadConversations() {
    try {
        state.conversations = await api('/api/conversations');
        sortConversations();
        renderSidebarList();
        updatePartnerHeader();
    } catch (error) {
        toast(error.message, true);
    }
}

function sortConversations() {
    state.conversations.sort((a, b) => new Date(b.updatedAt) - new Date(a.updatedAt));
}

function renderSidebarList() {
    const container = document.getElementById('conversationList');
    container.innerHTML = '';

    const query = state.searchQuery.toLowerCase();
    const filtered = query
        ? state.conversations.filter(item => item.otherUser.displayName.toLowerCase().includes(query))
        : state.conversations;

    if (!query && filtered.length === 0) {
        const empty = document.createElement('div');
        empty.className = 'list-empty';
        empty.textContent = 'Henüz sohbetiniz yok. Yukarıdaki kutudan isim veya e-posta ile kişi arayın.';
        container.appendChild(empty);
        return;
    }

    for (const conversation of filtered) {
        container.appendChild(buildConversationItem(conversation));
    }

    if (query) {
        const existingUserIds = new Set(state.conversations.map(item => item.otherUser.id));
        const extraUsers = state.searchUsers.filter(user => !existingUserIds.has(user.id));

        if (extraUsers.length > 0) {
            const title = document.createElement('div');
            title.className = 'list-section-title';
            title.textContent = 'Yeni Sohbet Başlat';
            container.appendChild(title);
            for (const user of extraUsers) {
                container.appendChild(buildUserSearchItem(user));
            }
        } else if (filtered.length === 0) {
            const empty = document.createElement('div');
            empty.className = 'list-empty';
            empty.textContent = state.searchQuery.length < 2
                ? 'Aramak için en az 2 harf yazın'
                : 'Eşleşen kişi bulunamadı';
            container.appendChild(empty);
        }
    }
}

function buildConversationItem(conversation) {
    const user = conversation.otherUser;
    const item = document.createElement('div');
    item.className = `user-item${conversation.id === state.activeConversationId ? ' active' : ''}`;
    item.addEventListener('click', () => selectConversation(conversation.id));

    const avatarWrap = document.createElement('div');
    avatarWrap.className = 'avatar-wrap';
    const avatar = document.createElement('img');
    avatar.className = 'avatar';
    avatar.alt = user.displayName;
    avatar.src = avatarSrc(user);
    avatarWrap.appendChild(avatar);
    if (user.isOnline) {
        const dot = document.createElement('span');
        dot.className = 'presence-dot';
        avatarWrap.appendChild(dot);
    }

    const body = document.createElement('div');
    body.className = 'user-item-body';

    const top = document.createElement('div');
    top.className = 'user-item-top';
    const name = document.createElement('span');
    name.className = 'user-item-name';
    name.textContent = user.displayName;
    const time = document.createElement('span');
    time.className = 'user-item-time';
    time.textContent = formatShortTime(conversation.lastMessage?.sentAt || conversation.updatedAt);
    top.append(name, time);

    const bottom = document.createElement('div');
    bottom.className = 'user-item-bottom';
    const sub = document.createElement('span');
    sub.className = 'user-item-sub';
    sub.textContent = state.typingUsers.has(conversation.id)
        ? 'Yazıyor...'
        : formatPreview(conversation.lastMessage, user.statusMessage);
    bottom.appendChild(sub);

    if (conversation.unreadCount > 0) {
        const badge = document.createElement('span');
        badge.className = 'unread-badge';
        badge.textContent = conversation.unreadCount > 99 ? '99+' : String(conversation.unreadCount);
        bottom.appendChild(badge);
    }

    body.append(top, bottom);
    item.append(avatarWrap, body);
    return item;
}

function buildUserSearchItem(user) {
    const item = document.createElement('div');
    item.className = 'user-item';
    item.addEventListener('click', () => startConversationWith(user.id));

    const avatar = document.createElement('img');
    avatar.className = 'avatar';
    avatar.alt = user.displayName;
    avatar.src = avatarSrc(user);

    const body = document.createElement('div');
    body.className = 'user-item-body';
    const name = document.createElement('div');
    name.className = 'user-item-name';
    name.textContent = user.displayName;
    const sub = document.createElement('div');
    sub.className = 'user-item-sub';
    sub.textContent = user.statusMessage || 'Sohbet başlatmak için tıklayın';
    body.append(name, sub);

    item.append(avatar, body);
    return item;
}

function formatPreview(lastMessage, fallbackStatus) {
    if (!lastMessage) return fallbackStatus || 'Henüz mesaj yok';
    if (lastMessage.isDeleted) return '🚫 Bu mesaj silindi';
    const prefix = lastMessage.senderId === state.me?.id ? 'Siz: ' : '';
    if (lastMessage.attachment && !lastMessage.content) {
        return `${prefix}📎 ${lastMessage.attachment.name}`;
    }
    return `${prefix}${lastMessage.content}`;
}

async function startConversationWith(userId) {
    try {
        const conversation = await api('/api/conversations', { method: 'POST', body: { userId } });
        if (!state.conversations.some(item => item.id === conversation.id)) {
            state.conversations.unshift(conversation);
        }
        clearUserSearch();
        await selectConversation(conversation.id);
    } catch (error) {
        toast(error.message, true);
    }
}

// ============================================================================
// Aktif Sohbet ve Mesajlar
// ============================================================================

async function selectConversation(conversationId) {
    const version = ++state.selectionVersion;
    state.activeConversationId = conversationId;
    state.inChatSearchMode = false;
    clearComposerMode();
    document.getElementById('chatSearchBar').hidden = true;
    document.getElementById('chatHeader').hidden = false;
    document.getElementById('chatInputContainer').hidden = false;
    document.getElementById('appContainer').classList.add('chat-open');

    updatePartnerHeader();
    renderSidebarList();

    const container = document.getElementById('chatMessages');
    container.innerHTML = '<div class="list-empty">Mesajlar yükleniyor...</div>';

    try {
        const list = await api(`/api/conversations/${conversationId}/messages?take=${PAGE_SIZE}`);
        if (version !== state.selectionVersion) return;

        state.messages = list;
        state.hasOlderMessages = list.length === PAGE_SIZE;
        renderMessages({ scrollToBottom: true });
        markConversationRead(conversationId);
    } catch (error) {
        if (version === state.selectionVersion) {
            container.innerHTML = '';
            const empty = document.createElement('div');
            empty.className = 'list-empty';
            empty.textContent = error.message;
            container.appendChild(empty);
        }
    }
}

async function reloadActiveMessages() {
    if (!state.activeConversationId) return;
    const list = await api(`/api/conversations/${state.activeConversationId}/messages?take=${PAGE_SIZE}`);
    state.messages = list;
    state.hasOlderMessages = list.length === PAGE_SIZE;
    renderMessages({ scrollToBottom: true });
}

async function loadOlderMessages() {
    if (!state.activeConversationId || state.messages.length === 0) return;
    const oldest = state.messages[0].sentAt;
    try {
        const older = await api(
            `/api/conversations/${state.activeConversationId}/messages?before=${encodeURIComponent(oldest)}&take=${PAGE_SIZE}`);
        state.hasOlderMessages = older.length === PAGE_SIZE;
        state.messages = [...older, ...state.messages];
        renderMessages({ preserveScroll: true });
    } catch (error) {
        toast(error.message, true);
    }
}

function markConversationRead(conversationId) {
    const conversation = state.conversations.find(item => item.id === conversationId);
    if (conversation) {
        conversation.unreadCount = 0;
        renderSidebarList();
    }
    api(`/api/conversations/${conversationId}/read`, { method: 'POST' }).catch(() => {});
}

function updatePartnerHeader() {
    const conversation = state.conversations.find(item => item.id === state.activeConversationId);
    if (!conversation) return;

    const partner = conversation.otherUser;
    document.getElementById('partnerName').textContent = partner.displayName;
    document.getElementById('partnerAvatar').src = avatarSrc(partner);

    const status = document.getElementById('partnerStatus');
    const isTyping = state.typingUsers.has(conversation.id);
    status.classList.toggle('online', partner.isOnline || isTyping);

    if (isTyping) {
        status.textContent = 'Yazıyor...';
    } else if (partner.isOnline) {
        status.textContent = partner.statusMessage ? `Çevrimiçi · ${partner.statusMessage}` : 'Çevrimiçi';
    } else if (partner.lastSeenAt) {
        status.textContent = `Son görülme: ${formatShortTime(partner.lastSeenAt)}`;
    } else {
        status.textContent = partner.statusMessage || 'Çevrimdışı';
    }
}

function resetChatPane() {
    state.activeConversationId = null;
    document.getElementById('appContainer').classList.remove('chat-open');
    document.getElementById('chatHeader').hidden = true;
    document.getElementById('chatInputContainer').hidden = true;
    document.getElementById('chatSearchBar').hidden = true;
    clearComposerMode();
    document.getElementById('chatMessages').innerHTML = `
        <div class="welcome-placeholder">
            <div class="icon-circle"><i class="bi bi-chat-left-text-fill"></i></div>
            <h2>Sohbet Odasına Hoş Geldiniz</h2>
            <p>Sol panelden bir sohbet seçin veya arama kutusuna isim ya da e-posta yazarak yeni bir kişi bulun.</p>
        </div>`;
    renderSidebarList();
}

function renderMessages({ scrollToBottom = false, preserveScroll = false } = {}) {
    const container = document.getElementById('chatMessages');
    const prevBottomOffset = container.scrollHeight - container.scrollTop;
    container.innerHTML = '';

    if (state.hasOlderMessages && !state.inChatSearchMode) {
        const olderBtn = document.createElement('button');
        olderBtn.className = 'load-older-btn';
        olderBtn.textContent = 'Daha eski mesajları yükle';
        olderBtn.addEventListener('click', loadOlderMessages);
        container.appendChild(olderBtn);
    }

    if (state.messages.length === 0) {
        const empty = document.createElement('div');
        empty.className = 'list-empty';
        empty.textContent = state.inChatSearchMode
            ? 'Aramanızla eşleşen mesaj bulunamadı.'
            : 'Henüz mesaj yok. İlk mesajı siz gönderin.';
        container.appendChild(empty);
        return;
    }

    for (const message of state.messages) {
        container.appendChild(buildMessageRow(message));
    }

    if (scrollToBottom) {
        container.scrollTop = container.scrollHeight;
    } else if (preserveScroll) {
        container.scrollTop = container.scrollHeight - prevBottomOffset;
    }
}

function buildMessageRow(message) {
    const isOutgoing = message.senderId === state.me.id;
    const row = document.createElement('div');
    row.className = `message-row ${isOutgoing ? 'justify-out' : 'justify-in'}`;

    const bubble = document.createElement('div');
    bubble.className = `msg-bubble ${isOutgoing ? 'bubble-out' : 'bubble-in'}${message.isDeleted ? ' deleted' : ''}`;

    if (message.isDeleted) {
        const deleted = document.createElement('div');
        deleted.className = 'message-content';
        deleted.textContent = '🚫 Bu mesaj silindi';
        bubble.appendChild(deleted);
    } else {
        if (message.attachment) {
            if (message.attachment.isImage) {
                const img = document.createElement('img');
                img.className = 'message-attachment-img';
                img.src = message.attachment.url;
                img.alt = message.attachment.name;
                img.loading = 'lazy';
                img.addEventListener('click', () => window.open(message.attachment.url, '_blank'));
                bubble.appendChild(img);
            } else {
                const link = document.createElement('a');
                link.className = 'message-attachment-file';
                link.href = `${message.attachment.url}?download=true`;
                link.innerHTML = '<i class="bi bi-file-earmark-arrow-down"></i> ';
                const fileName = document.createElement('span');
                fileName.textContent = `${message.attachment.name} (${formatBytes(message.attachment.size)})`;
                link.appendChild(fileName);
                bubble.appendChild(link);
            }
        }

        if (message.content) {
            const content = document.createElement('div');
            content.className = 'message-content';
            content.textContent = message.content;
            bubble.appendChild(content);
        }
    }

    const meta = document.createElement('div');
    meta.className = 'message-meta';

    if (message.editedAt && !message.isDeleted) {
        const edited = document.createElement('span');
        edited.className = 'message-edited';
        edited.textContent = 'düzenlendi';
        meta.appendChild(edited);
    }

    const time = document.createElement('span');
    time.textContent = formatShortTime(message.sentAt);
    meta.appendChild(time);

    if (isOutgoing && !message.isDeleted) {
        const tick = document.createElement('i');
        const isRead = message.status === 'read';
        const isDelivered = isRead || message.status === 'delivered';
        tick.className = `bi ${isDelivered ? 'bi-check2-all' : 'bi-check2'} message-status${isRead ? ' read' : ''}`;
        tick.title = isRead ? 'Okundu' : isDelivered ? 'Teslim edildi' : 'Gönderildi';
        meta.appendChild(tick);
    }

    bubble.appendChild(meta);
    row.appendChild(bubble);

    // Mesaj aksiyonları: kendi mesajlarımızda düzenle + sil; karşı tarafın mesajında "benden sil"
    const actions = document.createElement('div');
    actions.className = 'message-actions';

    if (isOutgoing && !message.isDeleted) {
        const editBtn = document.createElement('button');
        editBtn.className = 'message-action';
        editBtn.title = 'Düzenle';
        editBtn.innerHTML = '<i class="bi bi-pencil"></i>';
        editBtn.addEventListener('click', () => startEditingMessage(message));
        actions.appendChild(editBtn);
    }

    const deleteBtn = document.createElement('button');
    deleteBtn.className = 'message-action delete-action';
    deleteBtn.title = 'Sil';
    deleteBtn.innerHTML = '<i class="bi bi-trash3"></i>';
    deleteBtn.addEventListener('click', () => deleteMessage(message, isOutgoing && !message.isDeleted));
    actions.appendChild(deleteBtn);

    row.appendChild(actions);
    return row;
}

// ============================================================================
// Mesaj Gönderme, Düzenleme, Silme, Dosya Ekleme
// ============================================================================

function startEditingMessage(message) {
    state.pendingFile = null;
    state.editingMessage = message;
    const input = document.getElementById('messageInput');
    input.value = message.content;
    autoResizeTextarea(input);
    input.focus();
    showComposerBar(`✏️ Mesaj düzenleniyor: "${message.content.slice(0, 50)}"`);
}

function onAttachmentSelected(event) {
    const file = event.target.files[0];
    event.target.value = '';
    if (!file || !state.activeConversationId) return;

    state.editingMessage = null;
    state.pendingFile = file;
    showComposerBar(`📎 Ek dosya: ${file.name} (${formatBytes(file.size)}) — açıklama yazıp gönderebilirsiniz`);
    document.getElementById('messageInput').focus();
}

function showComposerBar(text) {
    document.getElementById('composerBarText').textContent = text;
    document.getElementById('composerBar').hidden = false;
}

function clearComposerMode() {
    state.pendingFile = null;
    state.editingMessage = null;
    document.getElementById('composerBar').hidden = true;
}

async function submitComposer() {
    const input = document.getElementById('messageInput');
    const sendBtn = document.getElementById('sendButton');
    const content = input.value.trim();
    const conversationId = state.activeConversationId;
    if (!conversationId) return;

    // 1) Düzenleme modu
    if (state.editingMessage) {
        const target = state.editingMessage;
        if (!content && !target.attachment) return;
        sendBtn.disabled = true;
        try {
            await api(`/api/messages/${target.id}`, { method: 'PUT', body: { content } });
            input.value = '';
            autoResizeTextarea(input);
            clearComposerMode();
        } catch (error) {
            toast(error.message, true);
        } finally {
            sendBtn.disabled = false;
        }
        return;
    }

    // 2) Dosya gönderme modu
    if (state.pendingFile) {
        const file = state.pendingFile;
        const formData = new FormData();
        formData.append('file', file);
        if (content) formData.append('caption', content);

        sendBtn.disabled = true;
        try {
            await api(`/api/conversations/${conversationId}/attachments`, { method: 'POST', body: formData });
            input.value = '';
            autoResizeTextarea(input);
            clearComposerMode();
        } catch (error) {
            toast(error.message, true);
        } finally {
            sendBtn.disabled = false;
        }
        return;
    }

    // 3) Normal metin mesajı
    if (!content) return;
    sendBtn.disabled = true;
    stopTypingSignal();
    try {
        await api(`/api/conversations/${conversationId}/messages`, { method: 'POST', body: { content } });
        input.value = '';
        autoResizeTextarea(input);
    } catch (error) {
        toast(error.message, true);
    } finally {
        sendBtn.disabled = false;
        input.focus();
    }
}

async function deleteMessage(message, canDeleteForEveryone) {
    let scope = 'me';
    if (canDeleteForEveryone) {
        const forEveryone = confirm('Bu mesaj herkesten silinsin mi?\n("İptal" derseniz sadece sizden silme sorulur.)');
        if (forEveryone) {
            scope = 'everyone';
        } else if (!confirm('Mesaj sadece sizden silinsin mi?')) {
            return;
        }
    } else if (!confirm('Bu mesaj sizden silinsin mi?')) {
        return;
    }

    try {
        await api(`/api/messages/${message.id}?scope=${scope}`, { method: 'DELETE' });
    } catch (error) {
        toast(error.message, true);
    }
}

// ============================================================================
// Sohbet İçi Arama
// ============================================================================

function toggleChatSearchBar() {
    const bar = document.getElementById('chatSearchBar');
    if (bar.hidden) {
        bar.hidden = false;
        document.getElementById('chatSearchInput').focus();
    } else {
        closeChatSearch();
    }
}

async function runChatSearch() {
    const query = document.getElementById('chatSearchInput').value.trim();
    if (!query || !state.activeConversationId) return;

    try {
        state.inChatSearchMode = true;
        state.messages = await api(
            `/api/conversations/${state.activeConversationId}/messages/search?q=${encodeURIComponent(query)}`);
        renderMessages({ scrollToBottom: true });
    } catch (error) {
        toast(error.message, true);
    }
}

async function closeChatSearch() {
    document.getElementById('chatSearchBar').hidden = true;
    document.getElementById('chatSearchInput').value = '';
    if (state.inChatSearchMode) {
        state.inChatSearchMode = false;
        await reloadActiveMessages();
    }
}

// ============================================================================
// Profil ve Ayarlar Penceresi
// ============================================================================

function openProfileModal() {
    if (!state.me) return;
    document.getElementById('profileNameInput').value = state.me.displayName;
    document.getElementById('profileStatusInput').value = state.me.statusMessage || '';
    document.getElementById('profileInvisibleInput').checked = state.me.isInvisible;
    document.getElementById('modalAvatar').src = avatarSrc(state.me);
    document.getElementById('profileModal').hidden = false;
}

async function saveProfile(event) {
    event.preventDefault();
    try {
        state.me = await api('/api/profile', {
            method: 'PUT',
            body: {
                displayName: document.getElementById('profileNameInput').value.trim(),
                statusMessage: document.getElementById('profileStatusInput').value.trim(),
                isInvisible: document.getElementById('profileInvisibleInput').checked,
            },
        });
        renderCurrentUser();
        toast('Profil güncellendi');
    } catch (error) {
        toast(error.message, true);
    }
}

async function uploadAvatar(event) {
    const file = event.target.files[0];
    event.target.value = '';
    if (!file) return;

    const formData = new FormData();
    formData.append('file', file);
    try {
        state.me = await api('/api/profile/avatar', { method: 'POST', body: formData });
        renderCurrentUser();
        toast('Profil fotoğrafı güncellendi');
    } catch (error) {
        toast(error.message, true);
    }
}

async function removeAvatar() {
    try {
        state.me = await api('/api/profile/avatar', { method: 'DELETE' });
        renderCurrentUser();
        toast('Profil fotoğrafı kaldırıldı');
    } catch (error) {
        toast(error.message, true);
    }
}

async function changePassword(event) {
    event.preventDefault();
    const currentPassword = document.getElementById('currentPasswordInput').value;
    const newPassword = document.getElementById('newPasswordInput').value;
    try {
        await api('/api/auth/change-password', { method: 'POST', body: { currentPassword, newPassword } });
        document.getElementById('passwordForm').reset();
        toast('Şifreniz güncellendi');
    } catch (error) {
        toast(error.message, true);
    }
}

// ============================================================================
// Yazıyor Sinyali ve Emoji
// ============================================================================

function sendTypingSignal() {
    if (!connection || connection.state !== 'Connected' || !state.activeConversationId) return;
    if (!isSendingTyping) {
        isSendingTyping = true;
        connection.invoke('Typing', state.activeConversationId, true).catch(() => {});
    }
    clearTimeout(typingSendTimeout);
    typingSendTimeout = setTimeout(stopTypingSignal, 1500);
}

function stopTypingSignal() {
    clearTimeout(typingSendTimeout);
    if (isSendingTyping && connection?.state === 'Connected' && state.activeConversationId) {
        isSendingTyping = false;
        connection.invoke('Typing', state.activeConversationId, false).catch(() => {});
    }
    isSendingTyping = false;
}

function buildEmojiPicker() {
    const popover = document.getElementById('emojiPopover');
    for (const emoji of EMOJIS) {
        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'emoji-item';
        btn.textContent = emoji;
        btn.addEventListener('click', () => {
            const input = document.getElementById('messageInput');
            input.setRangeText(emoji, input.selectionStart, input.selectionEnd, 'end');
            input.focus();
            popover.hidden = true;
        });
        popover.appendChild(btn);
    }
}

// ============================================================================
// Olay Bağlantıları ve Yardımcılar
// ============================================================================

function bindEvents() {
    document.getElementById('authForm').addEventListener('submit', handleAuthSubmit);
    document.getElementById('authSwitchLink').addEventListener('click', toggleAuthMode);
    document.getElementById('logoutButton').addEventListener('click', logout);

    // Kişi / sohbet arama
    const searchInput = document.getElementById('userSearchInput');
    const clearBtn = document.getElementById('clearUserSearchBtn');
    searchInput.addEventListener('input', () => {
        state.searchQuery = searchInput.value.trim();
        clearBtn.hidden = !state.searchQuery;
        clearTimeout(userSearchDebounce);
        if (state.searchQuery.length < 2) {
            state.searchUsers = [];
            renderSidebarList();
            return;
        }
        userSearchDebounce = setTimeout(async () => {
            try {
                state.searchUsers = await api(`/api/users/search?q=${encodeURIComponent(state.searchQuery)}`);
                renderSidebarList();
            } catch { /* yoksay */ }
        }, 220);
    });
    clearBtn.addEventListener('click', clearUserSearch);

    // Mesaj yazma
    const messageInput = document.getElementById('messageInput');
    messageInput.addEventListener('input', () => {
        autoResizeTextarea(messageInput);
        sendTypingSignal();
    });
    messageInput.addEventListener('keydown', (event) => {
        if (event.key === 'Enter' && !event.shiftKey) {
            event.preventDefault();
            submitComposer();
        } else if (event.key === 'Escape') {
            clearComposerMode();
        }
    });
    document.getElementById('sendButton').addEventListener('click', submitComposer);
    document.getElementById('composerBarCancel').addEventListener('click', clearComposerMode);

    // Dosya ve emoji
    document.getElementById('attachButton').addEventListener('click', () => document.getElementById('fileInput').click());
    document.getElementById('fileInput').addEventListener('change', onAttachmentSelected);
    document.getElementById('emojiButton').addEventListener('click', (event) => {
        event.stopPropagation();
        const popover = document.getElementById('emojiPopover');
        popover.hidden = !popover.hidden;
    });
    document.addEventListener('click', () => { document.getElementById('emojiPopover').hidden = true; });

    // Sohbet içi arama ve mobil geri
    document.getElementById('chatSearchButton').addEventListener('click', toggleChatSearchBar);
    document.getElementById('chatSearchCloseBtn').addEventListener('click', closeChatSearch);
    document.getElementById('chatSearchInput').addEventListener('keydown', (event) => {
        if (event.key === 'Enter') runChatSearch();
        if (event.key === 'Escape') closeChatSearch();
    });
    document.getElementById('mobileBackBtn').addEventListener('click', resetChatPane);

    // Profil modal
    document.getElementById('profileEditButton').addEventListener('click', openProfileModal);
    document.getElementById('currentUserAvatar').addEventListener('click', openProfileModal);
    document.getElementById('closeProfileModal').addEventListener('click', () => {
        document.getElementById('profileModal').hidden = true;
    });
    document.getElementById('profileModal').addEventListener('click', (event) => {
        if (event.target.id === 'profileModal') event.target.hidden = true;
    });
    document.getElementById('profileForm').addEventListener('submit', saveProfile);
    document.getElementById('passwordForm').addEventListener('submit', changePassword);
    document.getElementById('changeAvatarBtn').addEventListener('click', () => document.getElementById('profilePhotoInput').click());
    document.getElementById('profilePhotoInput').addEventListener('change', uploadAvatar);
    document.getElementById('removeAvatarBtn').addEventListener('click', removeAvatar);
}

function clearUserSearch() {
    document.getElementById('userSearchInput').value = '';
    document.getElementById('clearUserSearchBtn').hidden = true;
    state.searchQuery = '';
    state.searchUsers = [];
    renderSidebarList();
}

function autoResizeTextarea(textarea) {
    textarea.style.height = 'auto';
    textarea.style.height = `${Math.min(textarea.scrollHeight, 120)}px`;
}

async function api(url, { method = 'GET', body } = {}) {
    const options = { method, credentials: 'same-origin', headers: {} };
    if (body instanceof FormData) {
        options.body = body;
    } else if (body !== undefined) {
        options.headers['Content-Type'] = 'application/json';
        options.body = JSON.stringify(body);
    }

    const response = await fetch(url, options);
    if (response.status === 204) return null;

    let data = null;
    try { data = await response.json(); } catch { /* boş yanıt */ }

    if (!response.ok) {
        throw new Error(data?.error || `İşlem başarısız (${response.status})`);
    }
    return data;
}

function avatarSrc(user) {
    if (user?.avatarUrl) return user.avatarUrl;
    const initials = (user?.displayName || '?')
        .split(' ')
        .filter(Boolean)
        .slice(0, 2)
        .map(part => part[0].toUpperCase())
        .join('');
    const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="80" height="80">
        <rect width="100%" height="100%" fill="#312e81"/>
        <text x="50%" y="54%" dominant-baseline="middle" text-anchor="middle"
              fill="#fff" font-family="Inter,sans-serif" font-size="30" font-weight="600">${initials}</text>
    </svg>`;
    return `data:image/svg+xml;utf8,${encodeURIComponent(svg)}`;
}

function formatShortTime(value) {
    if (!value) return '';
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return '';
    const isToday = new Date().toDateString() === date.toDateString();
    return isToday
        ? date.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' })
        : date.toLocaleDateString('tr-TR', { day: '2-digit', month: '2-digit', year: 'numeric' });
}

function formatBytes(bytes) {
    if (!bytes) return '0 B';
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

function toast(message, isError = false) {
    const container = document.getElementById('toastContainer');
    const item = document.createElement('div');
    item.className = `toast${isError ? ' error' : ''}`;
    item.textContent = message;
    container.appendChild(item);
    setTimeout(() => item.remove(), 3500);
}
