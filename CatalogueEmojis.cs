namespace TALKORA.Core;

/// <summary>Les emojis proposés dans le sélecteur, rangés par catégorie. Tout est local : aucune connexion.</summary>
public static class CatalogueEmojis
{
    public record Categorie(string Icone, string Nom, IReadOnlyList<string> Emojis);

    private static IReadOnlyList<string> L(string emojis)
        => emojis.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    public static readonly IReadOnlyList<Categorie> Toutes = new List<Categorie>
    {
        new("😀", "Visages", L("😀 😃 😄 😁 😆 😅 😂 🤣 😊 😇 🙂 🙃 😉 😌 😍 🥰 😘 😗 😋 😛 😜 🤪 😝 🤑 🤗 🤭 🤫 🤔 🤐 😐 😑 😶 😏 😒 🙄 😬 😮‍💨 🤥 😔 😪 😴 😷 🤒 🤕 🤢 🥵 🥶 😵 🤯 🤠 🥳 😎 🤓 🧐 😕 😟 🙁 😮 😯 😲 😳 🥺 😦 😧 😨 😰 😥 😢 😭 😱 😖 😣 😞 😓 😩 😫 🥱 😤 😡 😠 🤬 😈 👿 💀 💩 🤡 👻 👽 🤖")),
        new("👍", "Gestes", L("👍 👎 👌 ✌️ 🤞 🤟 🤘 🤙 👈 👉 👆 👇 ☝️ ✋ 🤚 🖐️ 🖖 👋 🤝 🙏 👏 🙌 👐 🤲 ✍️ 💪 🦾 🙋 🙆 🙅 🤷 🤦 💁 🙇")),
        new("❤️", "Cœurs", L("❤️ 🧡 💛 💚 💙 💜 🖤 🤍 🤎 💔 ❣️ 💕 💞 💓 💗 💖 💘 💝 💟 ♥️ 💋 💌 🌹 🥀 💐")),
        new("🐶", "Animaux", L("🐶 🐱 🐭 🐹 🐰 🦊 🐻 🐼 🐨 🐯 🦁 🐮 🐷 🐸 🐵 🙈 🙉 🙊 🐔 🐧 🐦 🦅 🦉 🦋 🐝 🐞 🐢 🐍 🐙 🐬 🐳 🐘 🦒 🐎 🐑 🐐 🌱 🌳 🌴 🌸 🌻 🌞 🌙 ⭐ 🌈 ☔ ❄️ 🔥")),
        new("🍕", "Nourriture", L("🍏 🍎 🍌 🍉 🍇 🍓 🍒 🍑 🍍 🥭 🥥 🥑 🍅 🥕 🌽 🥔 🍞 🧀 🍳 🥞 🍗 🍖 🌭 🍔 🍟 🍕 🥪 🌮 🍝 🍜 🍣 🍤 🍚 🍦 🍰 🎂 🍫 🍬 🍩 🍪 ☕ 🍵 🥤 🍺 🍷 🥂")),
        new("⚽", "Activités", L("⚽ 🏀 🏈 ⚾ 🎾 🏐 🏉 🎱 🏓 🥊 🏆 🥇 🥈 🥉 🎯 🎮 🎲 🎵 🎶 🎤 🎧 🎸 🎹 🥁 🎬 🎨 📚 ✈️ 🚗 🚲 🏠 🎉 🎊 🎁 🎈")),
        new("💡", "Objets", L("📱 💻 🖥️ ⌨️ 🖱️ 📷 🎥 📞 ☎️ 📺 🔋 🔌 💡 🔦 📁 📂 📄 📝 ✏️ 📌 📎 🔒 🔓 🔑 🛠️ ⚙️ 💰 💳 ⏰ ⌛ 📅 📦 🧳 🛒")),
        new("✅", "Symboles", L("✅ ❌ ⭕ ❗ ❓ ‼️ ⚠️ 🚫 💯 🔴 🟠 🟡 🟢 🔵 🟣 ⚫ ⚪ ➕ ➖ ✖️ ➡️ ⬅️ ⬆️ ⬇️ 🔄 ✔️ ☑️ 🔔 🔕 ♻️ 🆗 🆕 🆒 🔝")),
    };
}
