import SwiftUI

@main
struct PcTvApp: App {
    var body: some Scene {
        WindowGroup { StreamScreen().ignoresSafeArea() }
    }
}

struct StreamScreen: UIViewControllerRepresentable {
    func makeUIViewController(context: Context) -> StreamViewController { StreamViewController() }
    func updateUIViewController(_ vc: StreamViewController, context: Context) {}
}
