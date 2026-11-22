import SwiftUI

struct AboutWidgetsView: View {
    let isShared: Bool

    var body: some View {
        Form {
            Section("איך מוסיפים ווידג'טים") {
                Text("לחצו לחיצה ארוכה על מסך הבית, בחרו \"+\" וחפשו \"אוצריא\". אפשר להוסיף כל ווידג'ט כמה פעמים.")
                Text("זמני היום ולוח עברי עובדים מיד, לפי ההגדרות שבאפליקציה הזאת.")
            }
            Section("ווידג'טים עם נתוני אוצריא") {
                Text("ספרים אחרונים, סימניות, שמור וזכור והלוח האישי מציגים נתונים מאוצריא. כדי שיופיעו, אוצריא צריכה לשתף אותם עם הווידג'ט.")
                Text("עד שזה יקרה הם מציגים הסבר במקום נתונים.")
                    .foregroundStyle(.secondary)
            }
            if !isShared {
                Section {
                    Label("הרשאת App Group חסרה (בנייה לא חתומה): ההגדרות לא יגיעו לווידג'טים", systemImage: "exclamationmark.triangle")
                        .foregroundStyle(.orange)
                }
            }
            Section("פרטיות") {
                Text("שום מידע אינו נשלח לשום מקום: אין שרתים, אין איסוף נתונים ואין חשבון.")
            }
        }
        .formStyle(.grouped)
    }
}
