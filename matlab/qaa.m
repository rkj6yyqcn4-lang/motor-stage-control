clc; clear;

%% =========================================================
% 一维位移台 PWM 驱动 + ORG/CW/CCW 限位保护
%
% 输出接线：
%   PWM1    -> 驱动器 PUL+
%   OUTPUT3 -> 驱动器 DIR+
%   GND     -> 驱动器 PUL- 和 DIR-
%
% 输入接线：
%   ORG -> 板卡 IN3（仅回原点时使用，正常运动不作为限位）
%   CW  -> 板卡 IN4
%   CCW -> 板卡 IN5
%
% 已确认方向：DIR=1 向下，DIR=0 向上。
% 输入按位读取：IN1=bit1、IN2=bit2，以此类推。
% 万用表红笔接 +24V、黑笔接信号线时，未触发读 24V、触发读 0V，
% 实机读取已确认：DiReadV12 在开关触发时返回的对应输入位为 0。
%% =========================================================

%% ================== 1. 用户参数区 ==================

dllPath = 'USBDAQ_DLL_V12X64.dll';
hPath   = 'HKDAQ_MATLAB.h';
libName = 'USBDAQ_DLL_V12X64';

PWM_CH = 1;                 % PWM1
DIR_CH = 2;                 % OUTPUT3（DLL 通道号与原 qaa.m 一致）

ORG_IN = 3;
CW_IN  = 4;
CCW_IN = 5;
INPUT_ACTIVE_STATE = 0;     % 实机确认：0=触发，1=未触发

DOWN_DIR = 1;               % 已确认：1 向下
UP_DIR   = 0;               % 已确认：0 向上

home_first       = true;    % 正式运动前是否先寻找 ORG
home_freq_Hz      = 50;     % 回原点频率
home_timeout_s    = 120;    % 回原点最长时间
home_leave_s      = 10;     % 如果启动时已压住 ORG，向下退出原点区的超时
run_test_move_after_home = test_move_default(); % 安全默认 false；确认无误可将函数返回值改 true

start_delay_s = 0;
move_dir      = 1;          % 后续测试运动：1 向下，0 向上
freq_Hz       = 100;
move_time_s   = 5;
duty_percent  = 50;
stop_duty_percent = 0;
poll_interval_s = 0.005;    % 每 5 ms 检查一次输入

%% ================== 2. 参数保护 ==================

if ~ismember(move_dir, [0 1])
    error('move_dir 只能是 1（向下）或 0（向上）');
end
if ~ismember(INPUT_ACTIVE_STATE, [0 1])
    error('INPUT_ACTIVE_STATE 只能是 0 或 1');
end
if freq_Hz <= 0 || home_freq_Hz <= 0
    error('PWM 频率必须大于 0');
end
if move_time_s <= 0 || home_timeout_s <= 0
    error('运动时间和回原点超时必须大于 0');
end
if duty_percent <= 0 || duty_percent >= 100
    error('运行占空比必须在 0~100 之间（不含端点）');
end

%% ================== 3. 加载并打开设备 ==================

if ~libisloaded(libName)
    loadlibrary(dllPath, hPath, 'alias', libName);
end
fprintf('DLL 加载成功\n');

ret = calllib(libName, 'OpenUsbV12');
if ret ~= 0
    error('OpenUsbV12 打开失败，返回值 = %d', ret);
end
fprintf('采集卡打开成功\n');
cleanObj = onCleanup(@() safe_close(libName, PWM_CH, freq_Hz, stop_duty_percent));

stop_pwm(libName, PWM_CH, freq_Hz, stop_duty_percent);
[raw, org, cw, ccw] = read_limits(libName, ORG_IN, CW_IN, CCW_IN, INPUT_ACTIVE_STATE);
print_limits(raw, org, cw, ccw);

%% ================== 4. 回 ORG 原点 ==================

if home_first
    fprintf('开始回原点：向上 DIR=%d，频率 %d Hz\n', UP_DIR, home_freq_Hz);

    % 若启动时已经处于 ORG 有效区，先向下离开，再从同一方向重新接近，
    % 这样重复定位更稳定。向下退出时由 CW/IN4 保护。
    if org
        fprintf('启动时 ORG/IN3 已触发，先向下退出原点区。\n');
        if cw
            error('CW/IN4 当前已触发，未启动向下 PWM。请检查机械位置或 INPUT_ACTIVE_STATE');
        end
        set_direction(libName, DIR_CH, DOWN_DIR);
        start_pwm(libName, PWM_CH, home_freq_Hz, duty_percent);
        t0 = tic;
        while true
            [~, org, cw, ~] = read_limits(libName, ORG_IN, CW_IN, CCW_IN, INPUT_ACTIVE_STATE);
            if ~org
                break;
            end
            if cw
                stop_pwm(libName, PWM_CH, home_freq_Hz, stop_duty_percent);
                error('退出 ORG 区时 CW/IN4 限位触发，已停止');
            end
            if toc(t0) > home_leave_s
                stop_pwm(libName, PWM_CH, home_freq_Hz, stop_duty_percent);
                error('向下退出 ORG 区超时，已停止');
            end
            pause(poll_interval_s);
        end
        stop_pwm(libName, PWM_CH, home_freq_Hz, stop_duty_percent);
        pause(0.1);
    end

    % 向上寻找 ORG。CCW/IN5 是向上硬限位；如果它在 ORG 之前触发则报错。
    [~, ~, ~, ccw] = read_limits(libName, ORG_IN, CW_IN, CCW_IN, INPUT_ACTIVE_STATE);
    if ccw
        error('CCW/IN5 当前已触发，未启动回原点 PWM。请检查机械位置或 INPUT_ACTIVE_STATE');
    end
    set_direction(libName, DIR_CH, UP_DIR);
    start_pwm(libName, PWM_CH, home_freq_Hz, duty_percent);
    t0 = tic;
    while true
        [raw, org, cw, ccw] = read_limits(libName, ORG_IN, CW_IN, CCW_IN, INPUT_ACTIVE_STATE);
        if org
            stop_pwm(libName, PWM_CH, home_freq_Hz, stop_duty_percent);
            fprintf('ORG/IN3 已触发，回原点完成。\n');
            print_limits(raw, org, cw, ccw);
            break;
        end
        if ccw
            stop_pwm(libName, PWM_CH, home_freq_Hz, stop_duty_percent);
            error('寻找 ORG 时 CCW/IN5 先触发，已停止');
        end
        if toc(t0) > home_timeout_s
            stop_pwm(libName, PWM_CH, home_freq_Hz, stop_duty_percent);
            error('寻找 ORG 超过 %.1f s，已停止', home_timeout_s);
        end
        pause(poll_interval_s);
    end
end

%% ================== 5. 延时后执行测试运动 ==================

if run_test_move_after_home
if start_delay_s > 0
    fprintf('延时 %.3f s 后开始运动\n', start_delay_s);
    pause(start_delay_s);
end

if move_dir == DOWN_DIR
    direction_name = '向下';
else
    direction_name = '向上';
end
fprintf('开始%s运动：PWM%d，频率 %.1f Hz，最长时间 %.3f s\n', ...
    direction_name, PWM_CH, freq_Hz, move_time_s);

[~, ~, cw, ccw] = read_limits(libName, ORG_IN, CW_IN, CCW_IN, INPUT_ACTIVE_STATE);
if move_dir == DOWN_DIR && cw
    error('CW/IN4 当前已触发，未启动向下 PWM');
end
if move_dir == UP_DIR && ccw
    error('CCW/IN5 当前已触发，未启动向上 PWM');
end
set_direction(libName, DIR_CH, move_dir);
start_pwm(libName, PWM_CH, freq_Hz, duty_percent);
t0 = tic;
stopped_by_limit = false;

while toc(t0) < move_time_s
    [~, ~, cw, ccw] = read_limits(libName, ORG_IN, CW_IN, CCW_IN, INPUT_ACTIVE_STATE);

    if move_dir == DOWN_DIR && cw
        fprintf('CW/IN4 限位触发，立即停止向下运动。\n');
        stopped_by_limit = true;
        break;
    end
    if move_dir == UP_DIR && ccw
        fprintf('CCW/IN5 限位触发，立即停止向上运动。\n');
        stopped_by_limit = true;
        break;
    end
    pause(poll_interval_s);
end

stop_pwm(libName, PWM_CH, freq_Hz, stop_duty_percent);
[raw, org, cw, ccw] = read_limits(libName, ORG_IN, CW_IN, CCW_IN, INPUT_ACTIVE_STATE);
print_limits(raw, org, cw, ccw);

if stopped_by_limit
    fprintf('运动因限位/原点信号提前结束。\n');
else
    fprintf('到达设定运动时间，PWM 已停止。\n');
end
else
    fprintf('安全默认：回原点后已停止，未执行定时测试运动。\n');
    fprintf('确认方向、限位和行程安全后，可将 test_move_default 函数返回值改为 true。\n');
end

%% ================== 6. 关闭设备 ==================

calllib(libName, 'CloseUsbV12');
fprintf('采集卡已关闭\n');

%% =========================================================
% 本地函数
%% =========================================================

function set_direction(libName, dir_ch, state)
    ret = calllib(libName, 'DoSetV12', uint8(dir_ch), uint8(state));
    if ret ~= 0
        error('OUTPUT%d 方向设置失败，返回值 = %d', dir_ch, ret);
    end
    pause(0.03);
end

function enabled = test_move_default()
    % 单独放在函数中，避免误运行脚本时自动执行回原点后的定时运动。
    enabled = false;
end

function start_pwm(libName, pwm_ch, freq_hz, duty)
    ret = calllib(libName, 'PWMOutSetV12', ...
        uint8(pwm_ch), uint32(freq_hz), single(duty));
    if ret ~= 0
        error('PWM%d 启动失败，返回值 = %d', pwm_ch, ret);
    end
end

function stop_pwm(libName, pwm_ch, freq_hz, stop_duty)
    ret = calllib(libName, 'PWMOutSetV12', ...
        uint8(pwm_ch), uint32(freq_hz), single(stop_duty));
    if ret ~= 0
        warning('PWM%d 停止时返回值 = %d', pwm_ch, ret);
    end
end

function [raw, org, cw, ccw] = read_limits(libName, org_in, cw_in, ccw_in, active_state)
    value_ptr = libpointer('uint8Ptr', uint8(0));
    ret = calllib(libName, 'DiReadV12', value_ptr);
    if ret ~= 0
        error('DiReadV12 读取失败，返回值 = %d', ret);
    end
    raw = uint8(value_ptr.Value);
    org  = bitget(raw, org_in)  == active_state;
    cw   = bitget(raw, cw_in)   == active_state;
    ccw  = bitget(raw, ccw_in)  == active_state;
end

function print_limits(raw, org, cw, ccw)
    fprintf('DI=0x%02X，ORG/IN3=%s，CW/IN4=%s，CCW/IN5=%s\n', ...
        raw, state_text(org), state_text(cw), state_text(ccw));
end

function text = state_text(active)
    if active
        text = '触发';
    else
        text = '未触发';
    end
end

function safe_close(libName, pwm_ch, freq_hz, stop_duty)
    try
        if libisloaded(libName)
            try
                calllib(libName, 'PWMOutSetV12', ...
                    uint8(pwm_ch), uint32(freq_hz), single(stop_duty));
            catch
            end
            try
                calllib(libName, 'CloseUsbV12');
            catch
            end
        end
    catch
    end
end
