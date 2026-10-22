package com.farukerata.kackaldi.i18n

abstract class Strings {
    abstract val back: String
    abstract val retry: String
    abstract val couldNotLoad: String
    abstract val cancel: String
    abstract val remove: String
    abstract val copy: String
    abstract val copied: String
    abstract val loading: String
    abstract val min: String
    abstract val heavyTraffic: String
    abstract fun updated(ago: String): String
    abstract val lightTheme: String
    abstract val darkTheme: String
    abstract val language: String
    abstract val somethingWrong: String
    abstract val notSignedIn: String
    abstract fun requestFailed(status: Int): String
    abstract val androidPhone: String
    abstract val noServer: String
    abstract val serverSlow: String

    abstract fun ago(seconds: Long): String
    abstract fun lower(text: String): String
    abstract fun seen(ago: String): String

    abstract fun stopsAway(n: Int): String
    abstract val atFirstStop: String
    abstract val notLeft: String
    abstract val endsHere: String
    abstract fun towards(name: String): String
    abstract fun nowAt(name: String): String

    abstract fun leaves(time: String): String
    abstract val planned: String
    abstract val nextTrips: String
    abstract val estimatedHere: String
    abstract val cancelled: String
    abstract fun cancelledTrip(stop: String, time: String): String
    abstract fun announcementCount(n: Int): String
    abstract val announcementsTitle: String
    abstract val showTimetable: String
    abstract val hideTimetable: String
    abstract fun departuresFrom(stop: String): String
    abstract val weekdays: String
    abstract val saturday: String
    abstract val sunday: String
    abstract val holidayTimetable: String
    abstract val noTrips: String
    abstract val timetableUnavailable: String
    abstract fun showMoreLines(n: Int): String
    abstract val showLess: String
    abstract fun nextDayTrips(day: String): String
    abstract val weekdayNames: List<String>

    abstract val saveStation: String
    abstract val unsaveStation: String
    abstract fun railTowards(name: String): String
    abstract fun railFrom(name: String): String
    abstract val railNow: String
    abstract val railPlanned: String
    abstract val railNoTrains: String
    abstract val rail: String
    abstract val yourStations: String

    abstract val alerts: String
    abstract val devices: String
    abstract val searchPlaceholder: String
    abstract val clearSearch: String
    abstract val yourAlerts: String
    abstract val recent: String
    abstract val clear: String
    abstract val emptyTitle: String
    abstract val emptyText: String
    abstract val yourStops: String
    abstract val yourLines: String
    abstract val bothDirections: String
    abstract val searchDown: String
    abstract val nothingFound: String
    abstract val nothingFoundText: String
    abstract val lines: String
    abstract val stops: String
    abstract val alreadySignedIn: String

    abstract val saveStop: String
    abstract val unsaveStop: String
    abstract val all: String
    abstract val noBus: String
    abstract fun linesHere(lines: String): String
    abstract fun waiting(lines: String): String
    abstract fun nextTry(seconds: Int): String
    abstract fun moreStops(n: Int): String
    abstract fun seeLine(line: String): String
    abstract val setAlert: String
    abstract val setAnAlert: String
    abstract fun watching(lines: String): String
    abstract fun alertWhen(summary: String): String
    abstract val stop: String
    abstract val allowNotifications: String
    abstract val allowAlarms: String
    abstract fun alertSaved(readyAt: String, repeat: String): String

    abstract val switchDirection: String
    abstract val saveLine: String
    abstract val unsaveLine: String
    abstract val tapStop: String
    abstract val noBusDirection: String
    abstract fun waitingAt(stop: String): String
    abstract val waitingAtFirst: String
    abstract fun after(stop: String): String
    abstract fun next(stop: String): String
    abstract fun toStop(stop: String): String
    abstract val toThisStop: String
    abstract val clearStop: String
    abstract val noBusToStop: String
    abstract fun passed(n: Int): String

    abstract val devicesTitle: String
    abstract val addDevice: String
    abstract val gettingCode: String
    abstract val getNewCode: String
    abstract val getCode: String
    abstract val codeExpiredText: String
    abstract val getCodeText: String
    abstract fun expiresIn(time: String): String
    abstract val otherDeviceHint: String
    abstract val thisDevice: String
    abstract val inUse: String
    abstract fun lastUsed(ago: String): String

    abstract val inviteLead: String
    abstract val inviteCode: String
    abstract val join: String
    abstract val joining: String
    abstract val inviteWrong: String
    abstract val haveLink: String
    abstract val linkLead: String
    abstract val linkCode: String
    abstract val link: String
    abstract val linking: String
    abstract val linkWrong: String
    abstract val haveInvite: String
    abstract val scannedQr: String

    abstract val alertsTitle: String
    abstract val noAlerts: String
    abstract val noAlertsText: String
    abstract val runningNow: String
    abstract val saved: String
    abstract val deleteAlert: String
    abstract fun readyAtRepeat(time: String, repeat: String, summary: String): String
    abstract fun repeat(days: Set<Int>): String

    abstract val triggerMinutes: String
    abstract val triggerStops: String
    abstract val triggerAtStop: String
    abstract val tellMeWhen: String
    abstract val minBefore: String
    abstract val stopsBefore: String
    abstract val start: String
    abstract val saveAlert: String
    abstract val startsEarly: String
    abstract fun moreLines(n: Int): String
    abstract val imReady: String
    abstract val now: String
    abstract fun atTime(time: String): String
    abstract val atATime: String
    abstract val repeatTitle: String
    abstract val noDays: String
    abstract fun runsEvery(days: Set<Int>): String
    abstract val set: String
    abstract val readyAtTitle: String
    abstract fun alreadyCloser(line: String?, count: Int): String
    abstract fun summary(atStop: String?, minutes: Int?, stops: Int): String
    abstract val days: List<String>
    abstract val firstStopOfLine: String
    abstract fun stopsBeforeYours(n: Int): String
    abstract val tellWhenReaches: String

    abstract fun lookingFor(lines: String): String
    abstract fun noneCameBy(lines: String, time: String): String
    abstract val retrying: String
    abstract fun lostTrack(line: String): String
    abstract fun then(line: String, eta: String): String
    abstract fun brief(line: String, minutes: String?, stops: String): String
    abstract fun briefFirstStop(line: String): String
    abstract fun waitingNext(lines: String): String
    abstract fun noneYet(lines: String): String
    abstract fun timeToGo(stop: String): String
    abstract fun alertAt(trigger: String): String
    abstract fun readyAtAlert(time: String, trigger: String): String
    abstract fun minChip(minutes: String): String
    abstract fun gone(line: String, stop: String, time: String): String
    abstract fun nextBus(line: String, eta: String): String
    abstract fun noOther(lines: String): String
    abstract fun inMinutes(minutes: String): String
    abstract fun leavePassed(line: String, stop: String): String
    abstract fun leaveMinutes(line: String, minutes: String): String
    abstract fun leaveWhere(line: String, where: String): String
    abstract fun triggerAt(atStop: String?, minutes: Int?, stops: Int): String
    abstract val ok: String
    abstract val missedIt: String
    abstract val watchedChannel: String
    abstract val watchedChannelText: String
    abstract val leaveChannel: String
    abstract val leaveChannelText: String

    abstract val update: String
    abstract val openSettings: String
    abstract val tryAgain: String
    abstract val later: String
    abstract fun downloading(version: String, percent: Int): String
    abstract fun installing(version: String): String
    abstract val updateFailed: String
    abstract fun updateReady(version: String): String
    abstract val updateAvailableText: String
    abstract val updateDownloadingText: String
    abstract val updatePermissionText: String
    abstract val updateInstallingText: String
    abstract val downloadFailed: String
    abstract val notInstalled: String
}
