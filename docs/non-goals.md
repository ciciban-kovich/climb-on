# Non-goals — climbing partner app, v1

What v1 deliberately does not do. Anything not in specs.md and not listed here is undecided, not excluded.

- N1. Registration or login via phone number.
- N2. The app does not verify climbing competence or belay certification.
- N3. The app does not serve users under 18.
- N4. The app does not verify age or identity; date of birth is self-declared.
- N5. The app does not recognise a returning person under a new identity, except as in S41. Blocks against a deleted account are not carried over.
- N6. There is no in-app appeal flow for report outcomes (see S44, S96).
- N7. v1 has no automated end-to-end UI tests. Exception: the accessibility scans in C60, which render pages but do not click through flows.
- N8. Linking an SSO identity to an existing password account.
- N9. Changing an account's login method. A climber who has lost access to their SSO provider can request erasure through support (S44, S45) and register again with the same address once it is freed (S84). The new account starts empty.
- N10. Nynorsk.
- N11. The app does not use profile data for marketing or advertising, sends no promotional emails, and does not sell or share personal data beyond what running the service requires (C13, C14).
- N12. No payments: no in-app purchases, subscriptions or premium tiers.
- N13. Invites and connections are always between exactly two climbers. No group sessions or group chats.
- N14. No ratings or reviews of climbers.
- N15. No search or browsing of climbers. A climber sees other climbers only through their suggestion list (S4), invites and connections.
- N16. Sent messages cannot be edited or deleted by their author. Messages are removed only by erasure (S38).
- N17. Chat is text only: no images, files or link previews. URLs in message text are displayed as links (C67).
- N18. The sender of a message is never shown whether it has been read. Read state (S75) exists only for S73.
- N19. No gym integrations: no booking, check-in or membership lookups.
- N20. No live location sharing. No location more precise than the postcode is collected.
- N21. No offline functionality beyond loading the cached client.
