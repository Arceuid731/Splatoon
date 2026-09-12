# Bibliothèque de couverture — suivi de réalisation

Objectif validé : analyser globalement toutes les sources pour préparer une bibliothèque
locale d'aides par mécanique. Les presets deviennent des documents source internes.
Pas de dépendance à Foretell. Publication de la version finale demandée par Yann le
12 septembre 2026 (« que j'update in-game »).

## Exigences à vérifier avant publication

- [x] Hibiya entier, préférence linguistique au niveau du contenu, cache incrémental.
- [x] Analyse des conditions actives, acteurs, actions, statuts, autres événements.
- [x] Déduplication structurelle des aides équivalentes, conservation des rôles complémentaires.
- [x] Préservation des dépendances internes, conditions de layout et groupes liés.
- [x] Recalcul global et remplacement des contributions obsolètes ; sauvegardes conservées.
- [x] Protection des installations existantes et modifications locales.
- [x] Bibliothèque persistante, activation par instance / rencontre / mécanique mémorisée.
- [x] Hiérarchie des instances du jeu, filtres, rencontres identifiées et autres aides.
- [x] Aucune prétention d'exhaustivité des boss et mécaniques.
- [x] Aperçu isolé des dessins ; limites des conditions dynamiques explicites.
- [x] Popup de couverture sans installation ; aides non classées et scripts présentés.
- [x] Suppression du badge Recommended et du flux de sélection de presets par défaut.
- [x] Tests moteur, persistance, intégration et audit des sources réelles.
- [ ] Build plugin, revue de l'interface, package, publication et manifeste in-game vérifiés.

## Principes d'implémentation

Les identifiants d'événements ne sont ni des preuves de bonne géométrie ni une
taxonomie exhaustive des combats. Les champs inactifs ne comptent pas comme
couverture. Une aide comporte sa condition complète et un rôle (zone, texte,
placement, lien…), avec sa provenance et ses variantes. Un groupe dépendant reste
indivisible. Les variantes contradictoires ne sont pas superposées automatiquement.
La bibliothèque rend par le moteur Splatoon ; elle ne réécrit pas les sources.

Les scripts restent du code exécutable et leur installation exige la revue déjà
prévue. Ils doivent apparaître dans la couverture avec un état distinct lorsqu'ils
ne sont pas analysables par mécanique ; ne jamais les annoncer couverts sur la
seule présence d'identifiants dans leur source.

## État initial vérifié

Branche `codex/preset-hub-upstream-review`, commit `842f54b1` après fusion upstream
`4930d305`. Version locale 3.9.2.30 non publiée. L'ancien sélecteur est encore fondé
sur des résumés de presets et exclut les familles installées. Le runtime d'origine
interprète séparément les filtres activés et leurs valeurs.

## Travail en cours (non publié)

Le moteur `LayoutCoverageAnalyzer` extrait des fragments autonomes et garde les
conditions liées. `CoveragePlanner` résout les conflits entre contributions pour
maximiser les aides distinctes. Le calcul incrémental et la persistance sont ajoutés.
Hibiya est élargi à tout le dépôt (migration version de catalogue 3).

Premier audit réel : 7 851 exports, 66 337 contributions, 505 territoires, environ
5 secondes ; aucun conflit d'AidId sélectionné ni recherche interrompue. Cela ne
prouve pas encore l'équivalence sémantique de toutes les variantes. Eden E3 conserve
un écart de 8 aides disponibles / 7 retenues dû aux dépendances : à examiner.

Ce premier état a été suivi de tests supplémentaires. La nouvelle vue Coverage,
le panneau d'entrée sans installation et l'aperçu ImGui sont raccordés mais doivent
encore être revus/testés. Pas de publication, pas de modification de la configuration
du jeu. L'ancienne version de package ne correspond pas à ce travail en cours.

Points à traiter avant toute livraison :
- Filtrage immédiat des sources désactivées dans le runtime et invalidation des plans.
- Transaction runtime : ne changer les remplacements de layouts qu'après préparation réussie.
- Préserver les désactivations des installations précédentes et les modifications locales.
- Vérifier les références de capture externes et affiner les dépendances (cas E3).
- Vérifier géométrie exacte de l'aperçu, primitives non prises en charge et textes.
- Éviter les recalculs/allocation à chaque frame UI ; afficher toutes les aides actives.
- Compléter les contrôles des scripts déjà chargés, sans inventer une analyse par mécanique.
- Ajouter tests persistance, incrémental, migration et runtime ; revue complète des flux UI.
- Retirer code de sélection de presets devenu inutilisé ; mettre à jour docs et version.
- Packaging, publication approuvée par la demande finale, manifeste et disponibilité en jeu.

## Vérification avant release v3.0.0

96 tests passent après retrait des huit tests de l'ancien sélecteur remplacé. Les
tests runtime compilent directement `PresetHubCoverage.cs` et `PresetHubInstaller.cs` :
source désactivée, échec de préparation, remplacement conservant la sauvegarde,
modification locale, opt-out initial, activation par territoire et scripts gérés.
Les tests géométriques vérifient les conventions du renderer, anneaux, cônes,
extrémités de lignes, recul et éléments de capture sans dessin.

L'audit de 7 851 exports a traité 505 territoires en environ six secondes. Aucun
conflit d'AidId dans les sélections ni dépassement du budget de recherche. Les 70
diagnostics sont des sources sans territoire explicite : elles ne sont pas supposées
globalement applicables. Les installations globales préexistantes sont en revanche
présentées et contrôlées dans leur zone courante. Aucun renvoi de capture externe
à un autre layout n'a été trouvé dans les caches analysés.

Eden E3 : 7 aides identifiées, 7 retenues, 14 alternatives. Le faux écart de la
première passe provenait des noms de capture traduits ; les signatures sont désormais
normalisées par leurs conditions. L'analyse structurelle n'est pas une validation
de la stratégie ou du timing de chaque auteur.

Revue des flux UI : tableaux pour les libellés longs, accès direct à l'aperçu,
contrôles mémorisés, choix avancés réversibles, erreurs de synchronisation visibles,
aucun ancien bouton Install dans les lignes de sources de layouts. Pas de rendu
capturé dans un client FFXIV ni de test de combat exécuté pendant cette session.

La publication et la vérification du manifeste public restent à consigner après
réussite des workflows et contrôle du zip distribué.
