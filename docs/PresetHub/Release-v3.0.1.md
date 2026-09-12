# Preset Hub v3.0.1 — Splatoon 3.9.2.31

Corrige le crash du jeu lors du dépliage d'A Realm Reborn dans Coverage. Certaines
zones ont une catégorie vide ; le binding ImGui plantait en lisant son libellé.
Les noms manquants ont désormais un libellé de remplacement et les nœuds utilisent
un identifiant distinct de leur texte.

L'onglet Installed devient Saved installations : il liste les anciens dessins
sauvegardés et les scripts installés, pas la couverture active. Source library
permet de consulter les documents d'origine et de revoir les scripts ; Sources
permet de choisir les dépôts utilisés. Les dessins disponibles se gèrent dans Coverage.

Validation : reproduction isolée du crash natif d'origine, puis rendu de neuf
arborescences imbriquées avec le vrai binding ImGui (libellés vides, espaces, Unicode
et caractères spéciaux), tests du moteur et compilation Release/x64. Le test de
rendu est désormais exécuté par le workflow de packaging Windows. La réouverture
dans le client du joueur reste à confirmer après mise à jour.
