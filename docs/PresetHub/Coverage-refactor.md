# Bibliothèque de couverture — suivi de réalisation

Objectif validé : analyser globalement toutes les sources pour préparer une bibliothèque
locale d'aides par mécanique. Les presets deviennent des documents source internes.
Pas de dépendance à Foretell. Publication de la version finale demandée par Yann le
12 septembre 2026 (« que j'update in-game »).

## Exigences à vérifier avant publication

- [ ] Hibiya entier, préférence linguistique au niveau du contenu, cache incrémental.
- [ ] Analyse des conditions actives, acteurs, actions, statuts, autres événements.
- [ ] Déduplication des aides équivalentes, conservation des aides complémentaires.
- [ ] Préservation des dépendances, conditions de layout et groupes de dessins liés.
- [ ] Recalcul global et remplacement des contributions obsolètes sans perte d'aides.
- [ ] Prise en compte et protection des installations existantes et modifications locales.
- [ ] Bibliothèque persistante, activation par instance / rencontre / mécanique mémorisée.
- [ ] Hiérarchie des instances du jeu, filtres, rencontres identifiées et autres aides.
- [ ] Aucune prétention d'exhaustivité des boss et mécaniques.
- [ ] Aperçu isolé des dessins ; limites des conditions dynamiques explicites.
- [ ] Popup d'entrée de couverture, aucune installation à choisir, toutes les aides du hub visibles.
- [ ] Suppression du badge Recommended et du flux de sélection de presets par défaut.
- [ ] Tests du moteur, persistance, intégration et régression sur les sources réelles.
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

77 tests core passent (10 nouveaux). Le plugin compile. La nouvelle vue Coverage,
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
