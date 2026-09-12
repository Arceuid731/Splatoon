# Preset Hub v3.0.2 — Splatoon 3.9.2.32

La couverture sauvegardée affiche maintenant « Coverage ready » dès son chargement,
avec sa date de calcul. Le texte « Preparing coverage » ne reste plus bloqué lorsque
les sources sont inchangées. Un échec d'actualisation des sources indique clairement
que la couverture sauvegardée reste utilisable.

Au démarrage et après chaque actualisation, les signatures des sources sont comparées
à la bibliothèque enregistrée avant toute analyse ou recherche de combinaisons.
Si elles sont identiques et que le format de bibliothèque est compatible, le résultat
enregistré est réutilisé sans recalcul global. Si elles changent, le calcul reste en
arrière-plan ; les analyses d'exports déjà présentes dans le cache mémoire sont réutilisées.
Ce cache par export n'est pas persistant : un changement après redémarrage peut encore
nécessiter une analyse complète. La couverture précédente reste utilisable pendant ce calcul.

Validation : 98 tests, dont le chargement d'une sauvegarde, son activation sans accès
réseau et sa réutilisation par un nouveau compilateur ; changements et suppressions
de sources, incompatibilité de format et annulation. Compilation Release/x64 et
test de rendu natif de l'arborescence dans le packaging Windows.
