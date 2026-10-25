// --- 🔐 LOGİN & REGISTER SİSTEMİ DEĞİŞKENLERİ VE FONKSİYONLARI 🔐 ---
let isRegisterMode = false;

// Giriş ve Kayıt Modları Arasında Akıcı Geçiş Yapma
function toggleAuthMode(event) {
    event.preventDefault();
    isRegisterMode = !isRegisterMode;

    const title = document.getElementById('authTitle');
    const subtitle = document.getElementById('authSubtitle');
    const nameField = document.getElementById('registerNameField');
    const submitBtn = document.getElementById('authSubmitBtn');
    const switchLink = document.getElementById('authSwitchLink');
    const switchText = document.getElementById('authSwitchText');
    const nameInput = document.getElementById('authName');

    if (isRegisterMode) {
        title.innerText = "Kayıt Ol";
        subtitle.innerText = "Evrene yeni bir siber hesap katın";
        nameField.style.display = "flex";
        nameInput.required = true;
        submitBtn.innerText = "Kayıt Ol & Bağlan";
        switchText.innerHTML = `Zaten hesabınız var mı? <a href="#" id="authSwitchLink" onclick="toggleAuthMode(event)">Giriş Yap</a>`;
    } else {
        title.innerText = "Kozmik Giriş";
        subtitle.innerText = "Siber mesajlaşma ağına bağlanın";
        nameField.style.display = "none";
        nameInput.required = false;
        submitBtn.innerText = "Giriş Yap";
        switchText.innerHTML = `Hesabınız yok mu? <a href="#" id="authSwitchLink" onclick="toggleAuthMode(event)">Kayıt Ol</a>`;
    }
}

async function submitAuth(url, body) {
    const response = await fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body)
    });

    let data = {};
    try {
        data = await response.json();
    } catch {
        data = {};
    }

    if (!response.ok) {
        throw new Error(data.error || 'Islem basarisiz');
    }

    return data;
}

async function handleAuth(event) {
    event.preventDefault();

    const email = document.getElementById('authEmail').value.trim();
    const password = document.getElementById('authPassword').value;
    const displayName = isRegisterMode ? document.getElementById('authName').value.trim() : "";

    try {
        const data = isRegisterMode
            ? await submitAuth('/api/auth/register', { displayName, email, password })
            : await submitAuth('/api/auth/login', { email, password });

        launchMainApp(data.id, data.displayName);
    } catch (err) {
        console.error(err);
        alert(err.message || 'Sunucuya ulasilamadi');
    }
}

// Giriş Başarılı Olduğunda Ekranları Değiştiren Motor Fonksiyon
function launchMainApp(userId, name) {
    // Giriş kutusunu pürüzsüzce kapat, mesajlaşmayı aç
    document.getElementById('authContainer').style.display = 'none';
    document.getElementById('appContainer').style.display = 'flex';

    // Üst profil alanını giriş yapan kişiye göre güncelle
    document.getElementById('currentUserName').innerText = name;
    document.getElementById('currentUserAvatar').src = `https://ui-avatars.com/api/?name=${encodeURIComponent(name)}&background=075E54&color=fff&bold=true`;

    window.currentUserId = userId;
}
