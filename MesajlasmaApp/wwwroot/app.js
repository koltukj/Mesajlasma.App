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

// Giriş veya Kayıt Formu Gönderildiğinde Çalışacak Kod
function handleAuth(event) {
    event.preventDefault();

    const email = document.getElementById('authEmail').value.trim();
    const password = document.getElementById('authPassword').value;
    const displayName = isRegisterMode ? document.getElementById('authName').value.trim() : "";

    if (isRegisterMode) {
        // --- 🟢 KAYIT OLMA LOGİĞİ ---
        // Yeni kayıt olan kullanıcıyı JSON simülasyonuna kaydediyoruz
        const newUserId = crypto.randomUUID();
        const newUser = {
            id: newUserId,
            displayName: displayName,
            email: email,
            lastSeenAt: new Date().toISOString()
        };
        chatData.users.push(newUser);

        // Giriş yapan aktif kullanıcıyı bu kişi ilan et
        launchMainApp(newUserId, displayName);
    } else {
        // --- 🔵 GİRİŞ YAPMA LOGİĞİ ---
        // JSON içindeki kayıtlı kullanıcılardan email kontrolü yapıyoruz
        const foundUser = chatData.users.find(u => u.email.toLowerCase() === email.toLowerCase());

        if (foundUser) {
            launchMainApp(foundUser.id, foundUser.displayName);
        } else {
            // Eğer JSON'da yoksa test kolaylığı için Ayşe Yılmaz olarak giriş yaptırıyoruz
            alert("Sistemde özel kayıt bulunamadı, geliştirici moduyla 'Ayşe Yılmaz' olarak bağlanıyorsunuz.");
            launchMainApp("7de85405-d3c2-4531-a785-377cbc1365bc", "Ayşe Yılmaz");
        }
    }
}

// Giriş Başarılı Olduğunda Ekranları Değiştiren Motor Fonksiyon
function launchMainApp(userId, name) {
    // Giriş kutusunu pürüzsüzce kapat, mesajlaşmayı aç
    document.getElementById('authContainer').style.display = 'none';
    document.getElementById('appContainer').style.display = 'flex';

    // Üst profil alanını giriş yapan kişiye göre güncelle
    document.getElementById('currentUserName').innerText = name;
    document.getElementById('currentUserAvatar').src = `https://ui-avatars.com{encodeURIComponent(name)}&background=075E54&color=fff&bold=true`;

    // Aktif kullanıcıyı ata ve sol listeyi tazeleyip başlat
    window.currentUserId = userId;
    init();
}
