window.aiChatStorage = {
    get: function (key) {
        return window.localStorage.getItem(key);
    },
    set: function (key, value) {
        window.localStorage.setItem(key, value);
    },
    remove: function (key) {
        window.localStorage.removeItem(key);
    }
};

window.aiChatUi = {
    _messageRailState: new WeakMap(),
    _resolveMessageRailState: function (element, threshold) {
        if (!element) {
            return null;
        }

        const existing = window.aiChatUi._messageRailState.get(element);
        if (existing) {
            if (typeof threshold === "number") {
                existing.threshold = threshold;
            }

            return existing;
        }

        const state = {
            threshold: typeof threshold === "number" ? threshold : 96,
            stickToBottom: true,
            onScroll: null
        };

        state.onScroll = function () {
            state.stickToBottom = window.aiChatUi.isNearBottom(element, state.threshold);
        };

        element.addEventListener("scroll", state.onScroll, { passive: true });
        state.stickToBottom = window.aiChatUi.isNearBottom(element, state.threshold);
        window.aiChatUi._messageRailState.set(element, state);
        return state;
    },
    registerMessageRail: function (element, threshold) {
        window.aiChatUi._resolveMessageRailState(element, threshold);
    },
    isNearBottom: function (element, threshold) {
        if (!element) {
            return true;
        }

        const limit = typeof threshold === "number" ? threshold : 96;
        const remaining = element.scrollHeight - element.clientHeight - element.scrollTop;
        return remaining <= limit;
    },
    shouldStickToBottom: function (element) {
        const state = window.aiChatUi._resolveMessageRailState(element);
        return state ? state.stickToBottom : true;
    },
    scrollToBottom: function (element, force, smooth) {
        if (!element) {
            return;
        }

        const state = window.aiChatUi._resolveMessageRailState(element);
        if (state && force) {
            state.stickToBottom = true;
        }

        element.scrollTo({
            top: element.scrollHeight,
            behavior: smooth ? "smooth" : "auto"
        });
    },
    scrollToBottomIfFollowing: function (element) {
        if (!element) {
            return false;
        }

        const state = window.aiChatUi._resolveMessageRailState(element);
        if (!state || !state.stickToBottom) {
            return false;
        }

        element.scrollTo({
            top: element.scrollHeight,
            behavior: "auto"
        });

        return true;
    },
    copyText: async function (text) {
        if (!text) {
            return;
        }

        await navigator.clipboard.writeText(text);
    },
    playCompletionSound: function () {
        if (typeof window === "undefined") {
            return false;
        }

        const AudioContextCtor = window.AudioContext || window.webkitAudioContext;
        if (!AudioContextCtor) {
            return false;
        }

        try {
            if (!window.aiChatUi._audioContext) {
                window.aiChatUi._audioContext = new AudioContextCtor();
            }

            const context = window.aiChatUi._audioContext;
            if (context.state === "suspended") {
                context.resume();
            }

            const now = context.currentTime;
            const oscillator = context.createOscillator();
            const gain = context.createGain();

            oscillator.type = "sine";
            oscillator.frequency.setValueAtTime(880, now);
            oscillator.frequency.exponentialRampToValueAtTime(1320, now + 0.08);

            gain.gain.setValueAtTime(0.0001, now);
            gain.gain.exponentialRampToValueAtTime(0.08, now + 0.01);
            gain.gain.exponentialRampToValueAtTime(0.0001, now + 0.18);

            oscillator.connect(gain);
            gain.connect(context.destination);

            oscillator.start(now);
            oscillator.stop(now + 0.2);

            return true;
        } catch {
            return false;
        }
    },
    notifyCompletion: async function (message) {
        window.aiChatUi.playCompletionSound();

        if (!message || typeof window === "undefined" || !("Notification" in window)) {
            return false;
        }

        if (Notification.permission === "default") {
            try {
                await Notification.requestPermission();
            } catch {
                return false;
            }
        }

        if (Notification.permission !== "granted") {
            return false;
        }

        new Notification("AIChatApp", {
            body: message,
            silent: true
        });

        return true;
    }
};
