using './main.bicep'

// The image actually deployed changes with every commit (CI updates it); this is only the starting point.
param image = 'ghcr.io/nelinkolya/tee-time-booking-demo:latest'

param sqlAdminLogin = 'nikolay.nelin@gmail.com'
param sqlAdminObjectId = '36941adb-ac49-4983-9cee-57638ea6a992'
