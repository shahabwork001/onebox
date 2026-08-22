const URL_PATTERN = /(https?:\/\/[^\s<>"']+|www\.[^\s<>"']+)/gi;

/** A message consisting only of emoji reads as a reaction, and every chat client shows it larger. */
const EMOJI_ONLY = /^(?:\p{Extended_Pictographic}|\p{Emoji_Component}|️|‍|\s){1,8}$/u;

const isEmojiOnly = (text: string) => {
  const trimmed = text.trim();
  return trimmed.length > 0 && EMOJI_ONLY.test(trimmed) && /\p{Extended_Pictographic}/u.test(trimmed);
};

/**
 * Customers paste tracking links and order pages constantly, and a link an agent cannot click is a
 * link they have to retype. Rendered by splitting on a pattern rather than by setting innerHTML, so
 * message text is never interpreted as markup.
 */
export function MessageText({ text }: { text: string }) {
  if (isEmojiOnly(text)) return <p className="bubble-emoji">{text}</p>;

  const parts = text.split(URL_PATTERN);
  return (
    <p>
      {parts.map((part, index) => {
        if (!part) return null;
        if (!URL_PATTERN.test(part)) {
          URL_PATTERN.lastIndex = 0;
          return <span key={index}>{part}</span>;
        }
        URL_PATTERN.lastIndex = 0;
        const href = part.startsWith("http") ? part : `https://${part}`;
        return (
          <a key={index} href={href} target="_blank" rel="noreferrer noopener" className="bubble-link">
            {part}
          </a>
        );
      })}
    </p>
  );
}
